using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// Full reconcile on startup / SignalR <c>data_changed</c>; local-only push on interval and note save.
/// One sync at a time; deferred full sync when an event arrives while busy.
/// </summary>
public sealed class CloudMappedSyncCoordinator
{
    private readonly CloudFolderSynchronizer _folderSynchronizer;
    private readonly CloudApiService _api;
    private readonly ObservableCollection<CloudSyncMapping> _syncMappings;
    private readonly Func<Task> _reloadCurrentFolderAsync;
    private readonly Action<string> _setStatus;
    private readonly Action<Exception>? _syncFullErrorReporter;
    private readonly Func<bool> _isAuthenticated;
    private readonly Func<string?> _getUserId;
    private readonly Action? _onAuthRequired;
    private readonly Action? _onSyncCompleted;
    private readonly Dispatcher _dispatcher;
    /// <summary>One sync at a time for all mapped folders.</summary>
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private CancellationTokenSource? _intervalCts;
    private int _syncActive;
    private int _deferredFullSync;
    private static readonly TimeSpan AutoSyncInterval = TimeSpan.FromSeconds(15);

    public CloudMappedSyncCoordinator(
        CloudFolderSynchronizer folderSynchronizer,
        CloudApiService api,
        ObservableCollection<CloudSyncMapping> syncMappings,
        Func<Task> reloadCurrentFolderAsync,
        Action<string> setStatus,
        Action<Exception>? syncFullErrorReporter,
        Dispatcher dispatcher,
        Func<bool>? isAuthenticated = null,
        Func<string?>? getUserId = null,
        Action? onAuthRequired = null,
        Action? onSyncCompleted = null)
    {
        _folderSynchronizer = folderSynchronizer;
        _api = api;
        _syncMappings = syncMappings;
        _reloadCurrentFolderAsync = reloadCurrentFolderAsync;
        _setStatus = setStatus;
        _syncFullErrorReporter = syncFullErrorReporter;
        _dispatcher = dispatcher;
        _isAuthenticated = isAuthenticated ?? (() => true);
        _getUserId = getUserId ?? (() => null);
        _onAuthRequired = onAuthRequired;
        _onSyncCompleted = onSyncCompleted;
        StartAutoSyncInterval();
    }

    /// <summary>Periodic local-only push (no remote scan). Skips while any sync is active.</summary>
    public void StartAutoSyncInterval(TimeSpan? interval = null)
    {
        StopAutoSyncInterval();
        var period = interval ?? AutoSyncInterval;
        if (period <= TimeSpan.Zero) return;
        var cts = new CancellationTokenSource();
        _intervalCts = cts;
        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(period, token);
                    if (!_isAuthenticated()) continue;
                    if (_syncMappings.Count == 0) continue;
                    if (Volatile.Read(ref _syncActive) != 0)
                        continue;
                    await PushLocalChangesAllAsync(notifyIfUploaded: true);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Next tick retries.
                }
            }
        }, token);
    }

    public void StopAutoSyncInterval()
    {
        try { _intervalCts?.Cancel(); } catch { /* ignore */ }
        _intervalCts?.Dispose();
        _intervalCts = null;
    }

    public void SuspendMappedRootWatchers() { }
    public void ResumeMappedRootWatchers() { }
    public void RestartFileWatchers() { }

    public Task RunFolderSynchronizeAsync(Guid folderId, string localPath) =>
        RunExclusiveAsync(() => SynchronizeFolderBodyAsync(folderId, localPath));

    public void StartManualSync(CloudSyncMapping mapping)
    {
        _ = Task.Run(() => RunFolderSynchronizeAsync(mapping.CloudFolderId, mapping.LocalPath));
    }

    public void StartForcePush(CloudSyncMapping mapping)
    {
        _ = Task.Run(() => RunExclusiveAsync(async () =>
        {
            if (!EnsureAuthenticatedForSync()) return;
            var ok = await _folderSynchronizer.ForcePushAsync(
                mapping,
                s => _dispatcher.InvokeAsync(() => _setStatus(s)),
                _syncFullErrorReporter,
                _onAuthRequired);
            await _dispatcher.InvokeAsync(() => _reloadCurrentFolderAsync());
            if (ok)
                await NotifyDataChangedAsync();
            _onSyncCompleted?.Invoke();
        }));
    }

    public void StartForcePull(CloudSyncMapping mapping)
    {
        _ = Task.Run(() => RunExclusiveAsync(async () =>
        {
            if (!EnsureAuthenticatedForSync()) return;
            await _folderSynchronizer.ForcePullAsync(
                mapping,
                s => _dispatcher.InvokeAsync(() => _setStatus(s)),
                _syncFullErrorReporter,
                _onAuthRequired);
            await _dispatcher.InvokeAsync(() => _reloadCurrentFolderAsync());
            _onSyncCompleted?.Invoke();
        }));
    }

    /// <summary>Full reconcile (startup / SignalR). If busy, coalesce one deferred full after current finishes.</summary>
    public Task SynchronizeAllMappedAsync() => RequestFullSyncAsync();

    public Task RequestFullSyncAsync()
    {
        if (Volatile.Read(ref _syncActive) != 0)
        {
            Interlocked.Exchange(ref _deferredFullSync, 1);
            return Task.CompletedTask;
        }
        return RunExclusiveAsync(SynchronizeAllBodyAsync);
    }

    /// <summary>Local-only push (interval / note save). Skips entirely if sync is already running.</summary>
    public Task RequestLocalPushAsync()
    {
        if (Volatile.Read(ref _syncActive) != 0)
            return Task.CompletedTask;
        return PushLocalChangesAllAsync(notifyIfUploaded: true);
    }

    private Task PushLocalChangesAllAsync(bool notifyIfUploaded) =>
        RunExclusiveAsync(async () =>
        {
            if (!EnsureAuthenticatedForSync()) return;
            var applied = await _folderSynchronizer.PushLocalChangesAllAsync(
                _syncMappings.ToList(),
                msg => { _ = _dispatcher.InvokeAsync(() => _setStatus(msg)); },
                _syncFullErrorReporter,
                () => _ = _dispatcher.InvokeAsync(() => _onAuthRequired?.Invoke()));

            if (_dispatcher.CheckAccess())
                await _reloadCurrentFolderAsync();
            else
                await _dispatcher.InvokeAsync(_reloadCurrentFolderAsync);

            if (notifyIfUploaded && applied > 0)
                await NotifyDataChangedAsync();

            try { _onSyncCompleted?.Invoke(); }
            catch { /* ignore */ }
        });

    private async Task RunExclusiveAsync(Func<Task> work)
    {
        await _syncGate.WaitAsync();
        Interlocked.Exchange(ref _syncActive, 1);
        try
        {
            await work();
            while (Interlocked.Exchange(ref _deferredFullSync, 0) == 1)
                await SynchronizeAllBodyAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _syncActive, 0);
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

        try { _onSyncCompleted?.Invoke(); }
        catch { /* ignore notifier errors */ }
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

        try { _onSyncCompleted?.Invoke(); }
        catch { /* ignore */ }
    }

    private async Task NotifyDataChangedAsync()
    {
        var userId = _getUserId();
        if (string.IsNullOrEmpty(userId)) return;
        try
        {
            await _api.SendRealtimeCommandAsync("data_changed", userId, new { source = "cloud_sync" });
        }
        catch
        {
            // Non-fatal — other devices will catch up on next full sync / interval.
        }
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
}
