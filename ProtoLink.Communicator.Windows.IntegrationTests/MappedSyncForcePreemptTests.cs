using System.Collections.ObjectModel;
using System.Net.Http;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Force Download must preempt a long exclusive Sync (tester FAIL 1.0.82 —
/// UI stuck Syncing… ≥12m; StartForcePull starved behind mutex).
/// Android SyncFlight.forcePreferred / acquireForForce parity.
/// </summary>
[Trait("Category", "Offline")]
public class MappedSyncForcePreemptTests
{
    [Fact]
    public async Task StartForcePull_WhileUnlimitedSyncActive_CancelsSyncAndRunsForce()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-force-preempt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fake = new ForcePreemptFake { HangSynchronizeAll = true };
            var mapping = new CloudSyncMapping
            {
                CloudFolderId = Guid.NewGuid(),
                LocalPath = root,
                CloudFolderName = "test-map"
            };
            var mappings = new ObservableCollection<CloudSyncMapping> { mapping };
            var statuses = new List<string>();

            var coordinator = new CloudMappedSyncCoordinator(
                fake,
                new CloudApiService(new HttpClient()),
                mappings,
                reloadCurrentFolderAsync: () => Task.CompletedTask,
                setStatus: s => { lock (statuses) statuses.Add(s); },
                syncFullErrorReporter: null,
                dispatcher: null,
                isAuthenticated: () => true,
                getUserId: () => null,
                onAuthRequired: null,
                onSyncCompleted: null,
                startInterval: false);

            var fullTask = coordinator.RequestFullSyncAsync();
            await fake.FullStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(coordinator.IsSyncActiveForTests);

            coordinator.StartForcePull(mapping);

            await fake.ForcePullStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(fake.FullWasCancelled);
            Assert.Equal(1, fake.ForcePullCount);

            fake.ReleaseForce.TrySetResult();
            await fullTask.WaitAsync(TimeSpan.FromSeconds(10));

            // Force finished; deferred full (if any) may run — Sync must not still hold forever.
            await WaitUntilAsync(() => !coordinator.IsSyncActiveForTests, TimeSpan.FromSeconds(10));
            Assert.False(coordinator.IsForcePreferredForTests);

            lock (statuses)
            {
                Assert.Contains(statuses, s => s.Contains("Force download", StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task StartForcePull_WhileSyncActive_DoesNotDrainDeferredFullBeforeForce()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-force-nodefer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fake = new ForcePreemptFake { HangSynchronizeAll = true };
            var mapping = new CloudSyncMapping
            {
                CloudFolderId = Guid.NewGuid(),
                LocalPath = root,
                CloudFolderName = "test-map"
            };
            var mappings = new ObservableCollection<CloudSyncMapping> { mapping };

            var coordinator = new CloudMappedSyncCoordinator(
                fake,
                new CloudApiService(new HttpClient()),
                mappings,
                reloadCurrentFolderAsync: () => Task.CompletedTask,
                setStatus: _ => { },
                syncFullErrorReporter: null,
                dispatcher: null,
                isAuthenticated: () => true,
                getUserId: () => null,
                onAuthRequired: null,
                onSyncCompleted: null,
                startInterval: false);

            var fullTask = coordinator.RequestFullSyncAsync();
            await fake.FullStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Coalesce another full while first hangs — without Force preempt this would drain after cancel.
            var second = coordinator.RequestFullSyncAsync();
            Assert.True(second.IsCompleted);
            Assert.True(coordinator.HasDeferredFullSyncForTests);

            coordinator.StartForcePull(mapping);
            await fake.ForcePullStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Force acquired before any second SynchronizeAll drain.
            Assert.Equal(1, fake.SynchronizeAllCount);
            Assert.Equal(1, fake.ForcePullCount);

            // Let deferred full after Force complete (do not re-hang SynchronizeAll).
            fake.HangSynchronizeAll = false;
            fake.ReleaseForce.TrySetResult();
            await fullTask.WaitAsync(TimeSpan.FromSeconds(15));
            await WaitUntilAsync(() => !coordinator.IsSyncActiveForTests, TimeSpan.FromSeconds(10));

            // Deferred full runs after Force clears preferred (on same exclusive hold).
            Assert.True(fake.SynchronizeAllCount >= 2);
            Assert.False(coordinator.HasDeferredFullSyncForTests);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task RequestFullSync_WhileForcePreferred_CoalescesWithoutStarting()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-force-defer-full-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fake = new ForcePreemptFake { HangForcePull = true };
            var mapping = new CloudSyncMapping
            {
                CloudFolderId = Guid.NewGuid(),
                LocalPath = root,
                CloudFolderName = "test-map"
            };
            var mappings = new ObservableCollection<CloudSyncMapping> { mapping };

            var coordinator = new CloudMappedSyncCoordinator(
                fake,
                new CloudApiService(new HttpClient()),
                mappings,
                reloadCurrentFolderAsync: () => Task.CompletedTask,
                setStatus: _ => { },
                syncFullErrorReporter: null,
                dispatcher: null,
                isAuthenticated: () => true,
                getUserId: () => null,
                onAuthRequired: null,
                onSyncCompleted: null,
                startInterval: false);

            coordinator.StartForcePull(mapping);
            await fake.ForcePullStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(coordinator.IsForcePreferredForTests);

            var full = coordinator.RequestFullSyncAsync();
            Assert.True(full.IsCompleted);
            Assert.True(coordinator.HasDeferredFullSyncForTests);
            Assert.Equal(0, fake.SynchronizeAllCount);

            fake.ReleaseForce.TrySetResult();
            await WaitUntilAsync(() => !coordinator.IsSyncActiveForTests, TimeSpan.FromSeconds(10));
            Assert.True(fake.SynchronizeAllCount >= 1);
            Assert.False(coordinator.IsForcePreferredForTests);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Live FAIL 1.0.85 FORCE_LOCK_WAIT_NO_ACQUIRE: Uploading local changes… held the
    /// exclusive lane; Force stayed on Waiting for sync lock… (hang-SynchronizeAll units
    /// alone did not cover this class).
    /// </summary>
    [Fact]
    public async Task StartForcePull_WhileUploadingPushActive_CancelsPushAndRunsForce()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-force-upload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fake = new ForcePreemptFake { HangPushLocal = true };
            var mapping = new CloudSyncMapping
            {
                CloudFolderId = Guid.NewGuid(),
                LocalPath = root,
                CloudFolderName = "test-map"
            };
            var mappings = new ObservableCollection<CloudSyncMapping> { mapping };
            var statuses = new List<string>();

            var coordinator = new CloudMappedSyncCoordinator(
                fake,
                new CloudApiService(new HttpClient()),
                mappings,
                reloadCurrentFolderAsync: () => Task.CompletedTask,
                setStatus: s => { lock (statuses) statuses.Add(s); },
                syncFullErrorReporter: null,
                dispatcher: null,
                isAuthenticated: () => true,
                getUserId: () => null,
                onAuthRequired: null,
                onSyncCompleted: null,
                startInterval: false);

            var pushTask = coordinator.RequestLocalPushAsync();
            await fake.PushStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(coordinator.IsSyncActiveForTests);

            coordinator.StartForcePull(mapping);
            await fake.ForcePullStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(fake.PushWasCancelled);
            Assert.Equal(1, fake.ForcePullCount);
            Assert.Equal(1, fake.PushLocalCount);

            fake.ReleaseForce.TrySetResult();
            await pushTask.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitUntilAsync(() => !coordinator.IsSyncActiveForTests, TimeSpan.FromSeconds(10));
            Assert.False(coordinator.IsForcePreferredForTests);

            lock (statuses)
            {
                Assert.Contains(statuses, s => s.Contains("Uploading local changes", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(statuses, s => s.Contains("Force download", StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// After preempt releases Uploading…, a racing RequestLocalPush must not re-steal the
    /// mutex while Force is preferred (interval/push race behind ForceAcquireTimeout).
    /// </summary>
    [Fact]
    public async Task RequestLocalPush_WhileForcePreferred_DoesNotStart()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-force-skip-push-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fake = new ForcePreemptFake { HangForcePull = true };
            var mapping = new CloudSyncMapping
            {
                CloudFolderId = Guid.NewGuid(),
                LocalPath = root,
                CloudFolderName = "test-map"
            };
            var mappings = new ObservableCollection<CloudSyncMapping> { mapping };

            var coordinator = new CloudMappedSyncCoordinator(
                fake,
                new CloudApiService(new HttpClient()),
                mappings,
                reloadCurrentFolderAsync: () => Task.CompletedTask,
                setStatus: _ => { },
                syncFullErrorReporter: null,
                dispatcher: null,
                isAuthenticated: () => true,
                getUserId: () => null,
                onAuthRequired: null,
                onSyncCompleted: null,
                startInterval: false);

            coordinator.StartForcePull(mapping);
            await fake.ForcePullStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(coordinator.IsForcePreferredForTests);

            await coordinator.RequestLocalPushAsync();
            Assert.Equal(0, fake.PushLocalCount);

            fake.ReleaseForce.TrySetResult();
            await WaitUntilAsync(() => !coordinator.IsSyncActiveForTests, TimeSpan.FromSeconds(10));
            Assert.False(coordinator.IsForcePreferredForTests);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(25);
        }

        Assert.True(predicate(), "condition not met before timeout");
    }

    private sealed class ForcePreemptFake : CloudFolderSynchronizer
    {
        public ForcePreemptFake() : base(new CloudApiService(new HttpClient())) { }

        public TaskCompletionSource FullStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PushStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ForcePullStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseForce { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FullWasCancelled { get; private set; }
        public bool PushWasCancelled { get; private set; }
        public int SynchronizeAllCount { get; private set; }
        public int PushLocalCount { get; private set; }
        public int ForcePullCount { get; private set; }
        public bool HangSynchronizeAll { get; set; }
        public bool HangPushLocal { get; set; }
        public bool HangForcePull { get; set; }

        public override async Task<bool> SynchronizeAllAsync(
            IEnumerable<CloudSyncMapping> mappings,
            Action<string> reportStatus,
            Action<Exception>? reportFullError,
            Action? onAuthRequired = null,
            CancellationToken cancellationToken = default)
        {
            reportStatus("Syncing…");
            SynchronizeAllCount++;
            FullStarted.TrySetResult();
            if (HangSynchronizeAll)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    FullWasCancelled = true;
                    throw;
                }
            }

            reportStatus("Sync complete.");
            return true;
        }

        public override async Task<int> PushLocalChangesAllAsync(
            IEnumerable<CloudSyncMapping> mappings,
            Action<string> reportStatus,
            Action<Exception>? reportFullError,
            Action? onAuthRequired = null,
            CancellationToken cancellationToken = default)
        {
            reportStatus("Uploading local changes…");
            PushLocalCount++;
            PushStarted.TrySetResult();
            if (HangPushLocal)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    PushWasCancelled = true;
                    reportStatus("Upload paused.");
                    throw;
                }
            }

            reportStatus("No local changes to upload.");
            return 0;
        }

        public override async Task<bool> ForcePullAsync(
            CloudSyncMapping mapping,
            Action<string> reportStatus,
            Action<Exception>? reportFullError,
            Action? onAuthRequired = null,
            CancellationToken cancellationToken = default)
        {
            reportStatus("Force download…");
            ForcePullCount++;
            ForcePullStarted.TrySetResult();
            if (HangForcePull || HangSynchronizeAll || HangPushLocal)
            {
                await ReleaseForce.Task.WaitAsync(cancellationToken);
            }

            reportStatus("Force download complete.");
            return true;
        }
    }
}
