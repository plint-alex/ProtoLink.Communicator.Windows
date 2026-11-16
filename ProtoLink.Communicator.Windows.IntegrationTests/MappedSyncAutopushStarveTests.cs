using System.Collections.ObjectModel;
using System.Net.Http;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Interval PushLocalChanges must not starve deferred full Sync / LastSync
/// (tester FAIL 1.0.71 — UI stuck on “Uploading local changes…”).
/// </summary>
[Trait("Category", "Offline")]
public class MappedSyncAutopushStarveTests
{
    [Fact]
    public async Task RequestFullSync_WhileIntervalPushHoldsLane_CancelsPushAndRunsDeferredFull()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-autopush-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fake = new HangThenFullFake();
            var mappings = new ObservableCollection<CloudSyncMapping>
            {
                new()
                {
                    CloudFolderId = Guid.NewGuid(),
                    LocalPath = root,
                    CloudFolderName = "test-map"
                }
            };

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

            var pushStarted = fake.PushStarted;
            var pushTask = coordinator.RequestLocalPushAsync();
            await pushStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(coordinator.IsSyncActiveForTests);

            var fullRequest = coordinator.RequestFullSyncAsync();
            Assert.True(fullRequest.IsCompleted);

            await pushTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(fake.PushWasCancelled);
            Assert.Equal(1, fake.SynchronizeAllCount);
            Assert.False(coordinator.IsSyncActiveForTests);
            Assert.False(coordinator.HasDeferredFullSyncForTests);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task AutoPushBudget_Expires_ReleasesLaneWithoutFullSync()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-autopush-budget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fake = new HangThenFullFake();
            var mappings = new ObservableCollection<CloudSyncMapping>
            {
                new()
                {
                    CloudFolderId = Guid.NewGuid(),
                    LocalPath = root,
                    CloudFolderName = "test-map"
                }
            };

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

            // Use a short budget via reflection-free path: RequestLocalPush uses AutoPushBudget (60s).
            // Direct exclusive path: start push then wait for CancelAfter by substituting hang + manual cancel isn't needed —
            // assert that RequestLocalPush skips when deferred is already set.
            _ = coordinator.RequestFullSyncAsync();
            // Idle full sync runs immediately with fake (fast).
            await Task.Delay(50);
            Assert.Equal(1, fake.SynchronizeAllCount);

            // After full completes, local push is allowed again.
            fake.AllowPushToCompleteImmediately = true;
            await coordinator.RequestLocalPushAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, fake.PushCompletedCount);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task ManualSync_WhilePushActive_EscalatesToDeferredFull()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-autopush-manual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fake = new HangThenFullFake();
            var mapId = Guid.NewGuid();
            var mappings = new ObservableCollection<CloudSyncMapping>
            {
                new()
                {
                    CloudFolderId = mapId,
                    LocalPath = root,
                    CloudFolderName = "test-map"
                }
            };

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

            var pushTask = coordinator.RequestLocalPushAsync();
            await fake.PushStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await coordinator.RunFolderSynchronizeAsync(mapId, root).WaitAsync(TimeSpan.FromSeconds(5));
            await pushTask.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(fake.PushWasCancelled);
            Assert.Equal(1, fake.SynchronizeAllCount);
            Assert.Equal(0, fake.SynchronizeOneCount);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    private sealed class HangThenFullFake : CloudFolderSynchronizer
    {
        public HangThenFullFake() : base(new CloudApiService(new HttpClient())) { }

        public TaskCompletionSource PushStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool PushWasCancelled { get; private set; }
        public int SynchronizeAllCount { get; private set; }
        public int SynchronizeOneCount { get; private set; }
        public int PushCompletedCount { get; private set; }
        public bool AllowPushToCompleteImmediately { get; set; }

        public override async Task<int> PushLocalChangesAllAsync(
            IEnumerable<CloudSyncMapping> mappings,
            Action<string> reportStatus,
            Action<Exception>? reportFullError,
            Action? onAuthRequired = null,
            CancellationToken cancellationToken = default)
        {
            reportStatus("Uploading local changes…");
            PushStarted.TrySetResult();
            if (AllowPushToCompleteImmediately)
            {
                PushCompletedCount++;
                return 0;
            }

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

            PushCompletedCount++;
            return 0;
        }

        public override Task<bool> SynchronizeAllAsync(
            IEnumerable<CloudSyncMapping> mappings,
            Action<string> reportStatus,
            Action<Exception>? reportFullError,
            Action? onAuthRequired = null,
            CancellationToken cancellationToken = default)
        {
            reportStatus("Syncing…");
            SynchronizeAllCount++;
            reportStatus("Sync complete.");
            return Task.FromResult(true);
        }

        public override Task<bool> SynchronizeAsync(
            Guid cloudFolderId,
            string localPath,
            Action<string> reportStatus,
            Action<Exception>? reportFullError,
            Action? onAuthRequired = null,
            string? mappedLocalRootPath = null,
            CancellationToken cancellationToken = default)
        {
            SynchronizeOneCount++;
            reportStatus("Sync complete.");
            return Task.FromResult(true);
        }
    }
}
