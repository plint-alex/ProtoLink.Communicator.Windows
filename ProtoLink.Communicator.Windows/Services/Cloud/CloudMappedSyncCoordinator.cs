using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Services;

public sealed class CloudMappedSyncCoordinator
{
    private readonly CloudFolderSynchronizer _folderSynchronizer;
    private readonly ObservableCollection<CloudSyncMapping> _syncMappings;
    private readonly Func<Task> _reloadCurrentFolderAsync;
    private readonly Action<string> _setStatus;
    private readonly Action<Exception>? _syncFullErrorReporter;
    private readonly Func<bool> _isAuthenticated;
    private readonly Action? _onAuthRequired;
    private readonly Dispatcher _dispatcher;
    /// <summary>One sync at a time for all mapped folders (manual, startup, watcher).</summary>
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly Dictionary<Guid, FileSystemWatcher> _mappingFolderWatchers = new();
    private readonly object _debounceGate = new();
    private CancellationTokenSource? _debounceCts;
    private int _syncActive;
    private int _resyncRequested;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(750);

    public CloudMappedSyncCoordinator(
        CloudFolderSynchronizer folderSynchronizer,
        ObservableCollection<CloudSyncMapping> syncMappings,
        Func<Task> reloadCurrentFolderAsync,
        Action<string> setStatus,
        Action<Exception>? syncFullErrorReporter,
        Dispatcher dispatcher,
        Func<bool>? isAuthenticated = null,
        Action? onAuthRequired = null)
    {
        _folderSynchronizer = folderSynchronizer;
        _syncMappings = syncMappings;
        _reloadCurrentFolderAsync = reloadCurrentFolderAsync;
        _setStatus = setStatus;
        _syncFullErrorReporter = syncFullErrorReporter;
        _dispatcher = dispatcher;
        _isAuthenticated = isAuthenticated ?? (() => true);
        _onAuthRequired = onAuthRequired;
    }

    public void SuspendMappedRootWatchers()
    {
        foreach (var w in _mappingFolderWatchers.Values)
            w.EnableRaisingEvents = false;
    }

    public void ResumeMappedRootWatchers()
    {
        foreach (var w in _mappingFolderWatchers.Values)
            w.EnableRaisingEvents = true;
    }

    public void RestartFileWatchers()
    {
        foreach (var w in _mappingFolderWatchers.Values)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _mappingFolderWatchers.Clear();

        foreach (var m in _syncMappings)
        {
            if (string.IsNullOrWhiteSpace(m.LocalPath)) continue;
            if (!Directory.Exists(m.LocalPath)) continue;
            var path = Path.GetFullPath(m.LocalPath);
            var w = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite |
                               NotifyFilters.Size
            };
            void OnFsChange(object _, FileSystemEventArgs e)
            {
                if (!string.IsNullOrEmpty(e.FullPath))
                    NotifyLocalPathChanged(e.FullPath);
            }
            w.Changed += OnFsChange;
            w.Created += OnFsChange;
            w.Deleted += OnFsChange;
            w.Renamed += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.FullPath))
                    NotifyLocalPathChanged(e.FullPath);
                if (!string.IsNullOrEmpty(e.OldFullPath))
                    NotifyLocalPathChanged(e.OldFullPath);
            };
            w.EnableRaisingEvents = true;
            _mappingFolderWatchers[m.CloudFolderId] = w;
        }
    }

    public void NotifyLocalPathChanged(string? localPathOrFile)
    {
        if (string.IsNullOrWhiteSpace(localPathOrFile)) return;
        if (!_isAuthenticated()) return;
        var full = Path.GetFullPath(localPathOrFile);
        var hit = false;
        foreach (var mapping in _syncMappings.ToList())
        {
            if (!IsLocalPathUnderMappedRoot(mapping.LocalPath, full)) continue;
            hit = true;
            break;
        }
        if (!hit) return;

        // While a sync is running (or about to), coalesce into a single follow-up pass.
        if (Volatile.Read(ref _syncActive) != 0)
        {
            Interlocked.Exchange(ref _resyncRequested, 1);
            return;
        }

        CancellationToken token;
        lock (_debounceGate)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            token = _debounceCts.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelay, token);
                await SynchronizeAllMappedAsync();
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    public Task RunFolderSynchronizeAsync(Guid folderId, string localPath) =>
        RunExclusiveSyncAsync(() => SynchronizeFolderBodyAsync(folderId, localPath));

    public void StartManualSync(CloudSyncMapping mapping)
    {
        _ = Task.Run(() => RunFolderSynchronizeAsync(mapping.CloudFolderId, mapping.LocalPath));
    }

    public Task SynchronizeAllMappedAsync() =>
        RunExclusiveSyncAsync(SynchronizeAllBodyAsync);

    private async Task RunExclusiveSyncAsync(Func<Task> work)
    {
        await _syncGate.WaitAsync();
        Interlocked.Exchange(ref _syncActive, 1);
        SuspendMappedRootWatchers();
        try
        {
            do
            {
                Interlocked.Exchange(ref _resyncRequested, 0);
                await work();
            }
            // At most one extra pass for changes that arrived during sync (notes save, etc.).
            while (Interlocked.Exchange(ref _resyncRequested, 0) == 1);
        }
        finally
        {
            Interlocked.Exchange(ref _syncActive, 0);
            ResumeMappedRootWatchers();
            _syncGate.Release();
        }
    }

    private async Task SynchronizeAllBodyAsync()
    {
        if (!EnsureAuthenticatedForSync()) return;

        await _folderSynchronizer.SynchronizeAllAsync(
            _syncMappings.ToList(),
            msg => { _ = _dispatcher.InvokeAsync(() => _setStatus(msg)); },
            _syncFullErrorReporter,
            () => _ = _dispatcher.InvokeAsync(() => _onAuthRequired?.Invoke()));

        if (_dispatcher.CheckAccess())
            await _reloadCurrentFolderAsync();
        else
            await _dispatcher.InvokeAsync(_reloadCurrentFolderAsync);
    }

    private async Task SynchronizeFolderBodyAsync(Guid folderId, string localPath)
    {
        if (!EnsureAuthenticatedForSync()) return;

        await _folderSynchronizer.SynchronizeAsync(
            folderId,
            localPath,
            msg => { _ = _dispatcher.InvokeAsync(() => _setStatus(msg)); },
            _syncFullErrorReporter,
            () => _ = _dispatcher.InvokeAsync(() => _onAuthRequired?.Invoke()),
            mappedLocalRootPath: localPath);

        if (_dispatcher.CheckAccess())
            await _reloadCurrentFolderAsync();
        else
            await _dispatcher.InvokeAsync(_reloadCurrentFolderAsync);
    }

    private bool EnsureAuthenticatedForSync()
    {
        if (_isAuthenticated()) return true;
        _ = _dispatcher.InvokeAsync(() =>
        {
            _setStatus("Please log in.");
            _onAuthRequired?.Invoke();
        });
        return false;
    }

    private static bool IsLocalPathUnderMappedRoot(string mappingRoot, string changedPath)
    {
        if (string.IsNullOrWhiteSpace(mappingRoot)) return false;
        var root = Path.GetFullPath(mappingRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var p = Path.GetFullPath(changedPath);
        if (string.Equals(p, root, StringComparison.OrdinalIgnoreCase)) return true;
        return p.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || p.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
