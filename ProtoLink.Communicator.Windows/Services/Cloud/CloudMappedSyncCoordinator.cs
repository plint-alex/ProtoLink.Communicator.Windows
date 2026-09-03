using System.Collections.Concurrent;
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
    private readonly Dispatcher _dispatcher;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _syncGateByFolder = new();
    private readonly Dictionary<Guid, FileSystemWatcher> _mappingFolderWatchers = new();

    public CloudMappedSyncCoordinator(
        CloudFolderSynchronizer folderSynchronizer,
        ObservableCollection<CloudSyncMapping> syncMappings,
        Func<Task> reloadCurrentFolderAsync,
        Action<string> setStatus,
        Action<Exception>? syncFullErrorReporter,
        Dispatcher dispatcher)
    {
        _folderSynchronizer = folderSynchronizer;
        _syncMappings = syncMappings;
        _reloadCurrentFolderAsync = reloadCurrentFolderAsync;
        _setStatus = setStatus;
        _syncFullErrorReporter = syncFullErrorReporter;
        _dispatcher = dispatcher;
    }

    /// <summary>Temporarily stop raising watcher events (e.g. while the app deletes mirror files so sync is not triggered for those paths).</summary>
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
        var full = Path.GetFullPath(localPathOrFile);
        foreach (var mapping in _syncMappings.ToList())
        {
            if (!IsLocalPathUnderMappedRoot(mapping.LocalPath, full)) continue;
            _ = Task.Run(() => RunFolderSynchronizeAsync(mapping.CloudFolderId, mapping.LocalPath));
        }
    }

    public Task RunFolderSynchronizeAsync(Guid folderId, string localPath)
    {
        var gate = _syncGateByFolder.GetOrAdd(folderId, _ => new SemaphoreSlim(1, 1));
        return SynchronizeWithGateAsync(folderId, localPath, gate);
    }

    public void StartManualSync(CloudSyncMapping mapping)
    {
        _ = Task.Run(() => RunFolderSynchronizeAsync(mapping.CloudFolderId, mapping.LocalPath));
    }

    private async Task SynchronizeWithGateAsync(Guid folderId, string localPath, SemaphoreSlim gate)
    {
        await gate.WaitAsync();
        try
        {
            await _folderSynchronizer.SynchronizeAsync(
                folderId,
                localPath,
                msg => { _ = _dispatcher.InvokeAsync(() => _setStatus(msg)); },
                _syncFullErrorReporter,
                mappedLocalRootPath: localPath);

            if (_dispatcher.CheckAccess())
                await _reloadCurrentFolderAsync();
            else
                await _dispatcher.InvokeAsync(_reloadCurrentFolderAsync);
        }
        finally
        {
            gate.Release();
        }
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
