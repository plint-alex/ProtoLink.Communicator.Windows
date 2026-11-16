using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// Full reconcile on startup / SignalR <c>data_changed</c>; local-only push on interval and note save.
/// One sync at a time; deferred full sync when an event arrives while busy.
/// Interval push is time-budgeted and cancelled when a full sync is requested so LastSync is not starved.
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
    private readonly Dispatcher? _dispatcher;
    private CancellationTokenSource? _intervalCts;
    private CancellationTokenSource? _exclusiveWorkCts;
    private readonly SemaphoreSlim _syncMutex = new(1, 1);
    private int _syncActive;
    private int _deferredFullSync;
    private static readonly TimeSpan AutoSyncInterval = TimeSpan.FromSeconds(15);
    /// <summary>Max exclusive-lane hold for interval/local push so deferred full sync can run.</summary>
    internal static readonly TimeSpan AutoPushBudget = TimeSpan.FromSeconds(60);

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
        : this(
            folderSynchronizer,
            api,
            syncMappings,
            reloadCurrentFolderAsync,
            setStatus,
            syncFullErrorReporter,
            dispatcher,
            isAuthenticated,
            getUserId,
            onAuthRequired,
            onSyncCompleted,
            startInterval: true)
    {
    }

    /// <summary>Test hook: null dispatcher runs reload/status callbacks inline (no WPF pump).</summary>
    internal CloudMappedSyncCoordinator(
        CloudFolderSynchronizer folderSynchronizer,
        CloudApiService api,
        ObservableCollection<CloudSyncMapping> syncMappings,
        Func<Task> reloadCurrentFolderAsync,
        Action<string> setStatus,
        Action<Exception>? syncFullErrorReporter,
        Dispatcher? dispatcher,
        Func<bool>? isAuthenticated,
        Func<string?>? getUserId,
        Action? onAuthRequired,
        Action? onSyncCompleted,
        bool startInterval = true)
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
        if (startInterval)
            StartAutoSyncInterval();
    }

    /// <summary>Periodic local-only push (no remote scan). Skips while any sync is active or full sync is deferred.</summary>
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
                    if (Volatile.Read(ref _deferredFullSync) != 0)
                        continue;
                    await PushLocalChangesAllAsync(notifyIfUploaded: true, budget: AutoPushBudget);
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

    public Task RunFolderSynchronizeAsync(Guid folderId, string localPath)
    {
        if (Volatile.Read(ref _syncActive) != 0)
        {
            // Per-mapping Sync must not queue forever behind interval push — escalate to deferred full.
            RequestDeferredFullAndCancelExclusiveWork();
            return Task.CompletedTask;
        }
        return RunExclusiveAsync(
            ct => SynchronizeFolderBodyAsync(folderId, localPath, ct),
            budget: null);
    }

    public void StartManualSync(CloudSyncMapping mapping)
    {
        _ = Task.Run(() => RunFolderSynchronizeAsync(mapping.CloudFolderId, mapping.LocalPath));
    }

    public void StartForcePush(CloudSyncMapping mapping)
    {
        _ = Task.Run(() => RunExclusiveAsync(async ct =>
        {
            if (!EnsureAuthenticatedForSync()) return;
            var ok = await _folderSynchronizer.ForcePushAsync(
                mapping,
                s => _ = MarshalFireAndForget(() => _setStatus(s)),
                _syncFullErrorReporter,
                _onAuthRequired,
                ct);
            await MarshalAsync(_reloadCurrentFolderAsync);
            if (ok)
                await NotifyDataChangedAsync();
            _onSyncCompleted?.Invoke();
        }, budget: null));
    }

    public void StartForcePull(CloudSyncMapping mapping)
    {
        _ = Task.Run(() => RunExclusiveAsync(async ct =>
        {
            if (!EnsureAuthenticatedForSync()) return;
            await _folderSynchronizer.ForcePullAsync(
                mapping,
                s => _ = MarshalFireAndForget(() => _setStatus(s)),
                _syncFullErrorReporter,
                _onAuthRequired,
                ct);
            await MarshalAsync(_reloadCurrentFolderAsync);
            _onSyncCompleted?.Invoke();
        }, budget: null));
    }

    /// <summary>Full reconcile (startup / SignalR). If busy, coalesce one deferred full after current finishes (and cancel interval push).</summary>
    public Task SynchronizeAllMappedAsync() => RequestFullSyncAsync();

    public Task RequestFullSyncAsync()
    {
        if (Volatile.Read(ref _syncActive) != 0)
        {
            RequestDeferredFullAndCancelExclusiveWork();
            return Task.CompletedTask;
        }
        return RunExclusiveAsync(ct => SynchronizeAllBodyAsync(ct), budget: null);
    }

    /// <summary>Local-only push (interval / note save). Skips entirely if sync is already running or full sync is deferred.</summary>
    public Task RequestLocalPushAsync()
    {
        if (Volatile.Read(ref _syncActive) != 0)
            return Task.CompletedTask;
        if (Volatile.Read(ref _deferredFullSync) != 0)
            return Task.CompletedTask;
        return PushLocalChangesAllAsync(notifyIfUploaded: true, budget: AutoPushBudget);
    }

    private void RequestDeferredFullAndCancelExclusiveWork()
    {
        Interlocked.Exchange(ref _deferredFullSync, 1);
        try { _exclusiveWorkCts?.Cancel(); }
        catch { /* ignore */ }
    }

    private Task PushLocalChangesAllAsync(bool notifyIfUploaded, TimeSpan? budget) =>
        RunExclusiveAsync(async ct =>
        {
            if (!EnsureAuthenticatedForSync()) return;
            var applied = await _folderSynchronizer.PushLocalChangesAllAsync(
                _syncMappings.ToList(),
                msg => { _ = MarshalFireAndForget(() => _setStatus(msg)); },
                _syncFullErrorReporter,
                () => _ = MarshalFireAndForget(() => _onAuthRequired?.Invoke()),
                ct);

            await MarshalAsync(_reloadCurrentFolderAsync);

            if (notifyIfUploaded && applied > 0)
                await NotifyDataChangedAsync();

            try { _onSyncCompleted?.Invoke(); }
            catch { /* ignore */ }
        }, budget);

    private async Task RunExclusiveAsync(Func<CancellationToken, Task> work, TimeSpan? budget)
    {
        // Only one sync at a time. Disk access is serialized per file by MappedFolderIoGate inside
        // SyncEngine — holding that gate here blocked note loads for the whole sync (network included).
        await _syncMutex.WaitAsync().ConfigureAwait(false);
        Interlocked.Exchange(ref _syncActive, 1);
        var workCts = new CancellationTokenSource();
        Volatile.Write(ref _exclusiveWorkCts, workCts);
        CancellationTokenSource? linked = null;
        try
        {
            var ct = workCts.Token;
            if (budget is { } b && b > TimeSpan.Zero)
            {
                linked = CancellationTokenSource.CreateLinkedTokenSource(workCts.Token);
                linked.CancelAfter(b);
                ct = linked.Token;
            }

            try
            {
                await work(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Preempted by full sync request or auto-push budget — drain deferred full below.
            }

            while (Interlocked.Exchange(ref _deferredFullSync, 0) == 1)
            {
                try
                {
                    await SynchronizeAllBodyAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Full sync itself should not be cancelled by push CTS; ignore stray cancel.
                }
            }
        }
        finally
        {
            linked?.Dispose();
            Interlocked.CompareExchange(ref _exclusiveWorkCts, null, workCts);
            workCts.Dispose();
            Interlocked.Exchange(ref _syncActive, 0);
            _syncMutex.Release();
        }
    }

    private async Task SynchronizeAllBodyAsync(CancellationToken ct)
    {
        if (!EnsureAuthenticatedForSync()) return;

        await _folderSynchronizer.SynchronizeAllAsync(
            _syncMappings.ToList(),
            msg => { _ = MarshalFireAndForget(() => _setStatus(msg)); },
            _syncFullErrorReporter,
            () => _ = MarshalFireAndForget(() => _onAuthRequired?.Invoke()),
            ct);

        await MarshalAsync(_reloadCurrentFolderAsync);

        try { _onSyncCompleted?.Invoke(); }
        catch { /* ignore notifier errors */ }
    }

    private async Task SynchronizeFolderBodyAsync(Guid folderId, string localPath, CancellationToken ct)
    {
        if (!EnsureAuthenticatedForSync()) return;

        await _folderSynchronizer.SynchronizeAsync(
            folderId,
            localPath,
            msg => { _ = MarshalFireAndForget(() => _setStatus(msg)); },
            _syncFullErrorReporter,
            () => _ = MarshalFireAndForget(() => _onAuthRequired?.Invoke()),
            mappedLocalRootPath: localPath,
            cancellationToken: ct);

        await MarshalAsync(_reloadCurrentFolderAsync);

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
        _ = MarshalFireAndForget(() =>
        {
            _setStatus("Please log in.");
            _onAuthRequired?.Invoke();
        });
        return false;
    }

    private Task MarshalAsync(Func<Task> action)
    {
        if (_dispatcher == null || _dispatcher.CheckAccess())
            return action();
        return _dispatcher.InvokeAsync(action).Task;
    }

    private Task MarshalFireAndForget(Action action)
    {
        if (_dispatcher == null || _dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }
        return _dispatcher.InvokeAsync(action).Task;
    }

    /// <summary>Test observation: exclusive lane currently held.</summary>
    internal bool IsSyncActiveForTests => Volatile.Read(ref _syncActive) != 0;

    /// <summary>Test observation: deferred full sync coalesced.</summary>
    internal bool HasDeferredFullSyncForTests => Volatile.Read(ref _deferredFullSync) != 0;
}
