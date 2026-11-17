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
/// Force Download/Upload preempts the exclusive holder (Android SyncFlight.forcePreferred parity) so
/// progressive prune+fill is not starved behind a long Syncing… pass.
/// Same-named CloudFolder heal (prefer-populated + cross-CloudRoot) runs before Sync/Force
/// exclusive/coalesce (Android requestFullSync parity) and persists map file before UI apply.
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
    private int _forcePreferred;
    private readonly object _deferredFolderGate = new();
    private Guid _deferredFolderId;
    private string _deferredFolderPath = "";
    private static readonly TimeSpan AutoSyncInterval = TimeSpan.FromSeconds(15);
    /// <summary>Max exclusive-lane hold for interval/local push so deferred full sync can run.</summary>
    internal static readonly TimeSpan AutoPushBudget = TimeSpan.FromSeconds(60);
    /// <summary>Bound wait for Force to win the exclusive lane after preempt (Android acquireForForce).</summary>
    internal static readonly TimeSpan ForceAcquireTimeout = TimeSpan.FromSeconds(120);

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

    public Task RunFolderSynchronizeAsync(Guid folderId, string localPath) =>
        RunFolderSynchronizeCoreAsync(folderId, localPath);

    /// <summary>
    /// Android parity: heal before exclusive/coalesce so remap persists even when Sync is deferred
    /// behind interval push (installed FAIL 1.0.84 — Uploading… held lane, heal never ran).
    /// </summary>
    private async Task RunFolderSynchronizeCoreAsync(Guid folderId, string localPath)
    {
        if (EnsureAuthenticatedForSync())
            await HealSameNamedMappingsAsync(CancellationToken.None).ConfigureAwait(false);

        var current = _syncMappings.FirstOrDefault(m =>
            !string.IsNullOrWhiteSpace(m.LocalPath)
            && string.Equals(
                Path.GetFullPath(m.LocalPath),
                Path.GetFullPath(localPath),
                StringComparison.OrdinalIgnoreCase));
        var syncId = current?.CloudFolderId ?? folderId;
        var syncPath = current?.LocalPath ?? localPath;

        if (Volatile.Read(ref _forcePreferred) != 0)
        {
            // Force owns the lane — queue folder Sync without cancelling Force.
            QueueDeferredFolder(syncId, syncPath);
            return;
        }

        if (Volatile.Read(ref _syncActive) != 0)
        {
            // User asked for THIS mapping. Do not escalate to SynchronizeAll — on 1.0.76
            // cancel+all-maps WalkRemote across ~15 roots never stamped LastSync within 15m
            // (EndBatch meta churn without SetLastSyncUtc).
            RequestDeferredFolderAndCancelExclusiveWork(syncId, syncPath);
            return;
        }
        await RunExclusiveAsync(
            ct => SynchronizeFolderBodyAsync(syncId, syncPath, ct, healAlreadyDone: true),
            budget: null).ConfigureAwait(false);
    }

    public void StartManualSync(CloudSyncMapping mapping)
    {
        _ = Task.Run(() => RunFolderSynchronizeAsync(mapping.CloudFolderId, mapping.LocalPath));
    }

    public void StartForcePush(CloudSyncMapping mapping)
    {
        _ = Task.Run(() => RunForceExclusiveAsync(async ct =>
        {
            if (!EnsureAuthenticatedForSync()) return;
            // Heal before Force work (and before any deferred Sync drain) so remap is on disk.
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
        }));
    }

    public void StartForcePull(CloudSyncMapping mapping)
    {
        _ = Task.Run(() => RunForceExclusiveAsync(async ct =>
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
        }));
    }

    /// <summary>Full reconcile (startup / SignalR). If busy, coalesce one deferred full after current finishes (and cancel interval push).</summary>
    public Task SynchronizeAllMappedAsync() => RequestFullSyncAsync();

    /// <summary>
    /// Android <c>requestFullSync</c> parity: heal <em>before</em> exclusive/coalesce so prefer-populated
    /// remap is persisted even when reconcile is deferred behind interval Uploading…
    /// </summary>
    public Task RequestFullSyncAsync() => RequestFullSyncCoreAsync();

    private async Task RequestFullSyncCoreAsync()
    {
        if (EnsureAuthenticatedForSync())
            await HealSameNamedMappingsAsync(CancellationToken.None).ConfigureAwait(false);

        if (Volatile.Read(ref _forcePreferred) != 0)
        {
            // Force wins — coalesce one full after Force releases (Android SyncFlight.deferredFull).
            Interlocked.Exchange(ref _deferredFullSync, 1);
            return;
        }

        if (Volatile.Read(ref _syncActive) != 0)
        {
            // Coalesce: cancel budgeted push only. Never abort an in-flight unlimited
            // reconcile just to restart SynchronizeAll from zero (1.0.76 LastSync hang class).
            // Heal already ran above — map file is correct even while Uploading… continues.
            Interlocked.Exchange(ref _deferredFullSync, 1);
            CancelBudgetedExclusiveOnly();
            return;
        }
        await RunExclusiveAsync(
            ct => SynchronizeAllBodyAsync(ct, healAlreadyDone: true),
            budget: null).ConfigureAwait(false);
    }

    /// <summary>Local-only push (interval / note save). Skips entirely if sync is already running or full sync is deferred.</summary>
    public Task RequestLocalPushAsync()
    {
        if (Volatile.Read(ref _forcePreferred) != 0)
            return Task.CompletedTask;
        if (Volatile.Read(ref _syncActive) != 0)
            return Task.CompletedTask;
        if (Volatile.Read(ref _deferredFullSync) != 0)
            return Task.CompletedTask;
        if (Volatile.Read(ref _deferredFolderSync) != 0)
            return Task.CompletedTask;
        return PushLocalChangesAllAsync(notifyIfUploaded: true, budget: AutoPushBudget);
    }

    private void QueueDeferredFolder(Guid folderId, string localPath)
    {
        lock (_deferredFolderGate)
        {
            _deferredFolderId = folderId;
            _deferredFolderPath = localPath;
        }
        Interlocked.Exchange(ref _deferredFolderSync, 1);
    }

    private void RequestDeferredFolderAndCancelExclusiveWork(Guid folderId, string localPath)
    {
        QueueDeferredFolder(folderId, localPath);
        // Cancel push OR slow all-maps so the requested folder can run and stamp LastSync.
        CancelExclusiveWork();
    }

    private void CancelBudgetedExclusiveOnly()
    {
        if (Volatile.Read(ref _budgetedExclusive) == 0) return;
        CancelExclusiveWork();
    }

    private void CancelExclusiveWork()
    {
        try { _exclusiveWorkCts?.Cancel(); }
        catch { /* ignore */ }
    }

    /// <summary>Android SyncFlight.preemptExclusiveForForce — mark preferred and cancel lane holder.</summary>
    private void PreemptExclusiveForForce()
    {
        Interlocked.Exchange(ref _forcePreferred, 1);
        CancelExclusiveWork();
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

    /// <summary>
    /// Force Download/Upload: preempt Sync holder, bound-wait for the mutex, run work, then clear preferred
    /// so deferred full/folder drains can proceed on this same exclusive hold.
    /// </summary>
    private async Task RunForceExclusiveAsync(Func<CancellationToken, Task> work)
    {
        PreemptExclusiveForForce();
        _ = MarshalFireAndForget(() => _setStatus("Waiting for sync lock…"));
        if (!await TryAcquireMutexForForceAsync().ConfigureAwait(false))
        {
            Interlocked.Exchange(ref _forcePreferred, 0);
            _ = MarshalFireAndForget(() => _setStatus("Timed out waiting for sync lock."));
            return;
        }

        Interlocked.Exchange(ref _syncActive, 1);
        var workCts = new CancellationTokenSource();
        Volatile.Write(ref _exclusiveWorkCts, workCts);
        try
        {
            try
            {
                await work(workCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Force cancelled (folder Sync after preferred cleared, or shut down).
            }

            // Force finished — allow deferred Sync drains on this hold (Android clearForcePreferred then deferredFull).
            Interlocked.Exchange(ref _forcePreferred, 0);
            await DrainDeferredWhileHoldingExclusiveAsync().ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _forcePreferred, 0);
            Interlocked.CompareExchange(ref _exclusiveWorkCts, null, workCts);
            workCts.Dispose();
            Interlocked.Exchange(ref _syncActive, 0);
            _syncMutex.Release();
        }
    }

    private async Task<bool> TryAcquireMutexForForceAsync()
    {
        var deadline = DateTime.UtcNow + ForceAcquireTimeout;
        while (DateTime.UtcNow < deadline)
        {
            CancelExclusiveWork();
            if (await _syncMutex.WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false))
                return true;
        }

        CancelExclusiveWork();
        return await _syncMutex.WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
    }

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
                // Preempted by folder/Force/auto-push budget — drain deferred below unless Force preferred.
            }

            // Force wants the lane: leave deferred flags set and release so Force can acquire promptly.
            if (Volatile.Read(ref _forcePreferred) != 0)
                return;

            await DrainDeferredWhileHoldingExclusiveAsync().ConfigureAwait(false);
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

    private async Task DrainDeferredWhileHoldingExclusiveAsync()
    {
        // Prefer user-requested folder Sync (LastSync for that map) before coalesced all-maps.
        while (Interlocked.Exchange(ref _deferredFolderSync, 0) == 1)
        {
            if (Volatile.Read(ref _forcePreferred) != 0)
            {
                Interlocked.Exchange(ref _deferredFolderSync, 1);
                return;
            }

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
                await SynchronizeFolderBodyAsync(folderId, localPath, folderCts.Token, healAlreadyDone: false)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Newer folder/full/Force request may re-queue below.
            }
            finally
            {
                Interlocked.CompareExchange(ref _exclusiveWorkCts, null, folderCts);
                folderCts.Dispose();
            }

            if (Volatile.Read(ref _forcePreferred) != 0)
                return;
        }

        while (Interlocked.Exchange(ref _deferredFullSync, 0) == 1)
        {
            if (Volatile.Read(ref _forcePreferred) != 0)
            {
                Interlocked.Exchange(ref _deferredFullSync, 1);
                return;
            }

            // Fresh CTS — push workCts is already cancelled; do not run reconcile on a
            // cancelled token (would sticky-fail before LastSync) or leave exclusive CTS stale.
            var fullCts = new CancellationTokenSource();
            Volatile.Write(ref _exclusiveWorkCts, fullCts);
            try
            {
                // Heal again on drain — covers deferred set without prior eager heal (Force path).
                await SynchronizeAllBodyAsync(fullCts.Token, healAlreadyDone: false).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Coalesced cancel from a newer folder Sync / Force — loop may run folder drain again.
            }
            finally
            {
                Interlocked.CompareExchange(ref _exclusiveWorkCts, null, fullCts);
                fullCts.Dispose();
            }

            if (Volatile.Read(ref _forcePreferred) != 0)
                return;

            // Folder Sync requested while draining full — run it before exiting the lane.
            while (Interlocked.Exchange(ref _deferredFolderSync, 0) == 1)
            {
                if (Volatile.Read(ref _forcePreferred) != 0)
                {
                    Interlocked.Exchange(ref _deferredFolderSync, 1);
                    return;
                }

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
                    await SynchronizeFolderBodyAsync(folderId, localPath, folderCts.Token, healAlreadyDone: false)
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

                if (Volatile.Read(ref _forcePreferred) != 0)
                    return;
            }
        }
    }

    private async Task SynchronizeAllBodyAsync(CancellationToken ct, bool healAlreadyDone = false)
    {
        if (!EnsureAuthenticatedForSync()) return;
        if (!healAlreadyDone)
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

    private async Task SynchronizeFolderBodyAsync(
        Guid folderId,
        string localPath,
        CancellationToken ct,
        bool healAlreadyDone = false)
    {
        if (!EnsureAuthenticatedForSync()) return;
        if (!healAlreadyDone)
            await HealSameNamedMappingsAsync(ct).ConfigureAwait(false);
        var healedId = folderId;
        var healedPath = localPath;
        var byLocal = _syncMappings.FirstOrDefault(m =>
            !string.IsNullOrWhiteSpace(m.LocalPath)
            && string.Equals(
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

    /// <summary>
    /// Android parity: heal before Sync/Force so twin maps converge on prefer-populated id.
    /// Persist to store <em>before</em> dispatcher UI collection update — installed FAIL 1.0.84
    /// class where runner remapped but cloud-sync-mappings.json stayed on the empty twin.
    /// </summary>
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
            var healed = result.Value.Healed.ToList();
            // Disk first — do not wait on dispatcher for the durable remap.
            _syncStore.Save(healed);
            void ApplyUi()
            {
                _syncMappings.Clear();
                foreach (var m in healed)
                    _syncMappings.Add(m);
            }

            if (_dispatcher != null)
                await _dispatcher.InvokeAsync(ApplyUi);
            else
                ApplyUi();
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

    /// <summary>Test observation: Force Download/Upload preferred over Sync.</summary>
    internal bool IsForcePreferredForTests => Volatile.Read(ref _forcePreferred) != 0;
}
