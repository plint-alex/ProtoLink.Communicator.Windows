using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services.Sync;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// Full reconcile on startup / SignalR <c>data_changed</c>; local-only push on interval and note save.
/// One sync at a time; deferred full sync when an event arrives while busy.
/// Interval push is time-budgeted and cancelled when a full sync is requested so LastSync is not starved.
/// Manual per-folder Sync while busy defers that folder (not SynchronizeAll) and may cancel the lane holder.
/// SignalR full while an unlimited reconcile runs coalesces without cancel+restart.
/// Same-named CloudFolder heal (prefer-populated + cross-CloudRoot) runs before Sync/Force start.
/// </summary>
public sealed class CloudMappedSyncCoordinator
{
    private readonly CloudFolderSynchronizer _folderSynchronizer;
    private readonly CloudApiService _api;
    private readonly ObservableCollection<CloudSyncMapping> _syncMappings;
    private readonly ISyncMappingStore? _syncStore;
    private readonly SameNamedCloudFolderHealRunner _healRunner;
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
    private int _deferredFolderSync;
    private int _budgetedExclusive;
    private readonly object _deferredFolderGate = new();
    private Guid _deferredFolderId;
    private string _deferredFolderPath = "";
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
        Action? onSyncCompleted = null,
        ISyncMappingStore? syncStore = null,
        SameNamedCloudFolderHealRunner? healRunner = null)
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
            startInterval: true,
            syncStore,
            healRunner)
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
        bool startInterval = true,
        ISyncMappingStore? syncStore = null,
        SameNamedCloudFolderHealRunner? healRunner = null)
    {
        _folderSynchronizer = folderSynchronizer;
        _api = api;
        _syncMappings = syncMappings;
        _syncStore = syncStore;
        _healRunner = healRunner ?? new SameNamedCloudFolderHealRunner(api);
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
                    if (Volatile.Read(ref _deferredFolderSync) != 0)
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
            // User asked for THIS mapping. Do not escalate to SynchronizeAll — on 1.0.76
            // cancel+all-maps WalkRemote across ~15 roots never stamped LastSync within 15m
            // (EndBatch meta churn without SetLastSyncUtc).
            RequestDeferredFolderAndCancelExclusiveWork(folderId, localPath);
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
            await HealSameNamedMappingsAsync(ct).ConfigureAwait(false);
            var current = ResolveMappingAfterHeal(mapping);
            if (current == null) return;
            var ok = await _folderSynchronizer.ForcePushAsync(
                current,
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
            await HealSameNamedMappingsAsync(ct).ConfigureAwait(false);
            var current = ResolveMappingAfterHeal(mapping);
            if (current == null) return;
            await _folderSynchronizer.ForcePullAsync(
                current,
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
            // Coalesce: cancel budgeted push only. Never abort an in-flight unlimited
            // reconcile just to restart SynchronizeAll from zero (1.0.76 LastSync hang class).
            Interlocked.Exchange(ref _deferredFullSync, 1);
            CancelBudgetedExclusiveOnly();
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
        if (Volatile.Read(ref _deferredFolderSync) != 0)
            return Task.CompletedTask;
        return PushLocalChangesAllAsync(notifyIfUploaded: true, budget: AutoPushBudget);
    }

    private void RequestDeferredFolderAndCancelExclusiveWork(Guid folderId, string localPath)
    {
        lock (_deferredFolderGate)
        {
            _deferredFolderId = folderId;
            _deferredFolderPath = localPath;
        }
        Interlocked.Exchange(ref _deferredFolderSync, 1);
        // Cancel push OR slow all-maps so the requested folder can run and stamp LastSync.
        try { _exclusiveWorkCts?.Cancel(); }
        catch { /* ignore */ }
    }

    private void CancelBudgetedExclusiveOnly()
    {
        if (Volatile.Read(ref _budgetedExclusive) == 0) return;
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
        var budgeted = budget is { } b0 && b0 > TimeSpan.Zero;
        if (budgeted)
            Interlocked.Exchange(ref _budgetedExclusive, 1);
        try
        {
            var ct = workCts.Token;
            if (budgeted)
            {
                linked = CancellationTokenSource.CreateLinkedTokenSource(workCts.Token);
                linked.CancelAfter(budget!.Value);
                ct = linked.Token;
            }

            try
            {
                await work(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Preempted by folder/full request or auto-push budget — drain deferred work below.
            }

            // Prefer user-requested folder Sync (LastSync for that map) before coalesced all-maps.
            while (Interlocked.Exchange(ref _deferredFolderSync, 0) == 1)
            {
                Guid folderId;
                string localPath;
                lock (_deferredFolderGate)
                {
                    folderId = _deferredFolderId;
                    localPath = _deferredFolderPath;
                }

                var folderCts = new CancellationTokenSource();
                Volatile.Write(ref _exclusiveWorkCts, folderCts);
                try
                {
                    await SynchronizeFolderBodyAsync(folderId, localPath, folderCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Newer folder/full request may re-queue below.
                }
                finally
                {
                    Interlocked.CompareExchange(ref _exclusiveWorkCts, null, folderCts);
                    folderCts.Dispose();
                }
            }

            while (Interlocked.Exchange(ref _deferredFullSync, 0) == 1)
            {
                // Fresh CTS — push workCts is already cancelled; do not run reconcile on a
                // cancelled token (would sticky-fail before LastSync) or leave exclusive CTS stale.
                var fullCts = new CancellationTokenSource();
                Volatile.Write(ref _exclusiveWorkCts, fullCts);
                try
                {
                    await SynchronizeAllBodyAsync(fullCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Coalesced cancel from a newer folder Sync — loop may run folder drain again.
                }
                finally
                {
                    Interlocked.CompareExchange(ref _exclusiveWorkCts, null, fullCts);
                    fullCts.Dispose();
                }

                // Folder Sync requested while draining full — run it before exiting the lane.
                while (Interlocked.Exchange(ref _deferredFolderSync, 0) == 1)
                {
                    Guid folderId;
                    string localPath;
                    lock (_deferredFolderGate)
                    {
                        folderId = _deferredFolderId;
                        localPath = _deferredFolderPath;
                    }

                    var folderCts = new CancellationTokenSource();
                    Volatile.Write(ref _exclusiveWorkCts, folderCts);
                    try
                    {
                        await SynchronizeFolderBodyAsync(folderId, localPath, folderCts.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // ignore
                    }
                    finally
                    {
                        Interlocked.CompareExchange(ref _exclusiveWorkCts, null, folderCts);
                        folderCts.Dispose();
                    }
                }
            }
        }
        finally
        {
            linked?.Dispose();
            if (budgeted)
                Interlocked.Exchange(ref _budgetedExclusive, 0);
            Interlocked.CompareExchange(ref _exclusiveWorkCts, null, workCts);
            workCts.Dispose();
            Interlocked.Exchange(ref _syncActive, 0);
            _syncMutex.Release();
        }
    }

    private async Task SynchronizeAllBodyAsync(CancellationToken ct)
    {
        if (!EnsureAuthenticatedForSync()) return;
        await HealSameNamedMappingsAsync(ct).ConfigureAwait(false);

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
        await HealSameNamedMappingsAsync(ct).ConfigureAwait(false);
        var healedId = folderId;
        var healedPath = localPath;
        var byLocal = _syncMappings.FirstOrDefault(m =>
            string.Equals(
                Path.GetFullPath(m.LocalPath),
                Path.GetFullPath(localPath),
                StringComparison.OrdinalIgnoreCase));
        if (byLocal != null)
        {
            healedId = byLocal.CloudFolderId;
            healedPath = byLocal.LocalPath;
        }

        await _folderSynchronizer.SynchronizeAsync(
            healedId,
            healedPath,
            msg => { _ = MarshalFireAndForget(() => _setStatus(msg)); },
            _syncFullErrorReporter,
            () => _ = MarshalFireAndForget(() => _onAuthRequired?.Invoke()),
            mappedLocalRootPath: healedPath,
            cancellationToken: ct);

        await MarshalAsync(_reloadCurrentFolderAsync);

        try { _onSyncCompleted?.Invoke(); }
        catch { /* ignore */ }
    }

    /// <summary>Android parity: heal before Sync/Force so twin maps converge on prefer-populated id.</summary>
    private async Task HealSameNamedMappingsAsync(CancellationToken ct)
    {
        if (_syncStore == null || _syncMappings.Count == 0) return;
        var userRaw = _getUserId();
        if (string.IsNullOrWhiteSpace(userRaw) || !Guid.TryParse(userRaw, out var userId) || userId == Guid.Empty)
            return;
        try
        {
            var snapshot = _syncMappings.ToList();
            var result = await _healRunner.TryHealAsync(snapshot, userId, ct).ConfigureAwait(false);
            if (result is null) return;
            var healed = result.Value.Healed;
            void Apply()
            {
                _syncMappings.Clear();
                foreach (var m in healed)
                    _syncMappings.Add(m);
                _syncStore.Save(_syncMappings);
            }

            if (_dispatcher != null)
                await _dispatcher.InvokeAsync(Apply);
            else
                Apply();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"same-named heal soft-fail: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private CloudSyncMapping? ResolveMappingAfterHeal(CloudSyncMapping requested)
    {
        var byLocal = _syncMappings.FirstOrDefault(m =>
            !string.IsNullOrWhiteSpace(m.LocalPath)
            && !string.IsNullOrWhiteSpace(requested.LocalPath)
            && string.Equals(
                Path.GetFullPath(m.LocalPath),
                Path.GetFullPath(requested.LocalPath),
                StringComparison.OrdinalIgnoreCase));
        if (byLocal != null) return byLocal;
        return _syncMappings.FirstOrDefault(m => m.CloudFolderId == requested.CloudFolderId)
               ?? requested;
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

    /// <summary>Test observation: deferred per-folder Sync coalesced.</summary>
    internal bool HasDeferredFolderSyncForTests => Volatile.Read(ref _deferredFolderSync) != 0;
}
