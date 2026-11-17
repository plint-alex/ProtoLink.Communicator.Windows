using System.Collections.ObjectModel;
using System.Net.Http;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Sync;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Installed FAIL 1.0.84: live TryHealAsync remapped, but Sync/startup behind interval
/// Uploading… never persisted cloud-sync-mappings.json (heal only ran inside exclusive body).
/// </summary>
[Trait("Category", "Offline")]
public class MappedSyncHealPersistTests
{
    private static readonly Guid DupId = Guid.Parse("0bc549dd-c880-42df-af32-4f3c340ba970");
    private static readonly Guid CanonicalId = Guid.Parse("898c1453-4034-4b9f-96f6-430b4ceca476");
    private static readonly Guid UserId = Guid.Parse("a52b6095-7952-4535-90a1-ce8bdaf7f9c7");

    [Fact]
    public async Task RequestFullSync_WhilePushHoldsLane_HealsAndPersistsBeforeSynchronizeAll()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-heal-persist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fakeSync = new HangPushFake();
            var store = new MemorySyncMappingStore(() => fakeSync.SynchronizeAllCount);
            var heal = new RemapHealRunner(DupId, CanonicalId, root);
            var mappings = new ObservableCollection<CloudSyncMapping>
            {
                new()
                {
                    CloudFolderId = DupId,
                    LocalPath = root,
                    CloudFolderName = "MyNotesFolder"
                }
            };

            var coordinator = new CloudMappedSyncCoordinator(
                fakeSync,
                new CloudApiService(new HttpClient()),
                mappings,
                reloadCurrentFolderAsync: () => Task.CompletedTask,
                setStatus: _ => { },
                syncFullErrorReporter: null,
                dispatcher: null,
                isAuthenticated: () => true,
                getUserId: () => UserId.ToString(),
                onAuthRequired: null,
                onSyncCompleted: null,
                startInterval: false,
                syncStore: store,
                healRunner: heal);

            var pushStarted = fakeSync.PushStarted;
            var pushTask = coordinator.RequestLocalPushAsync();
            await pushStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(coordinator.IsSyncActiveForTests);

            // Startup/SignalR Sync while Uploading… — must persist remap without waiting for SynchronizeAll.
            var fullTask = coordinator.RequestFullSyncAsync();
            await WaitUntilAsync(() => store.LastSaved != null, TimeSpan.FromSeconds(5));

            Assert.True(heal.TryHealCount >= 1);
            Assert.NotNull(store.LastSaved);
            Assert.Single(store.LastSaved!);
            Assert.Equal(CanonicalId, store.LastSaved![0].CloudFolderId);
            Assert.Equal(CanonicalId, mappings[0].CloudFolderId);
            // Persist happened at or before first SynchronizeAll (eager heal before coalesce/drain).
            Assert.True(
                store.SynchronizeAllCountAtSave == 0 || store.SynchronizeAllCountAtSave == fakeSync.SynchronizeAllCount,
                $"save ordering unexpected: atSave={store.SynchronizeAllCountAtSave} now={fakeSync.SynchronizeAllCount}");
            Assert.Equal(0, store.SynchronizeAllCountAtSave);

            await fullTask.WaitAsync(TimeSpan.FromSeconds(5));

            // Cancelled push drains deferred full — heal already persisted.
            try { await pushTask.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) { /* preempted */ }
            await WaitUntilAsync(() => !coordinator.IsSyncActiveForTests, TimeSpan.FromSeconds(10));
            Assert.Equal(CanonicalId, store.Load()[0].CloudFolderId);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task RequestFullSync_MissingUserId_SkipsHealPersist()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-heal-nouser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new MemorySyncMappingStore();
            var fakeSync = new HangPushFake { AllowPushToCompleteImmediately = true };
            var heal = new RemapHealRunner(DupId, CanonicalId, root);
            var mappings = new ObservableCollection<CloudSyncMapping>
            {
                new()
                {
                    CloudFolderId = DupId,
                    LocalPath = root,
                    CloudFolderName = "MyNotesFolder"
                }
            };

            var coordinator = new CloudMappedSyncCoordinator(
                fakeSync,
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
                startInterval: false,
                syncStore: store,
                healRunner: heal);

            await coordinator.RequestFullSyncAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(0, heal.TryHealCount);
            Assert.Null(store.LastSaved);
            Assert.Equal(DupId, mappings[0].CloudFolderId);
            Assert.Equal(1, fakeSync.SynchronizeAllCount);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> pred, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (pred()) return;
            await Task.Delay(25);
        }

        Assert.True(pred(), "condition not met before timeout");
    }

    private sealed class MemorySyncMappingStore : ISyncMappingStore
    {
        private readonly Func<int>? _syncAllCount;

        public MemorySyncMappingStore(Func<int>? syncAllCount = null) => _syncAllCount = syncAllCount;

        public List<CloudSyncMapping>? LastSaved { get; private set; }
        public int SynchronizeAllCountAtSave { get; private set; } = -1;

        public IReadOnlyList<CloudSyncMapping> Load() =>
            LastSaved ?? new List<CloudSyncMapping>();

        public void Save(IEnumerable<CloudSyncMapping> mappings)
        {
            if (SynchronizeAllCountAtSave < 0)
                SynchronizeAllCountAtSave = _syncAllCount?.Invoke() ?? 0;
            LastSaved = mappings.Select(m => new CloudSyncMapping
            {
                CloudFolderId = m.CloudFolderId,
                LocalPath = m.LocalPath,
                CloudFolderName = m.CloudFolderName
            }).ToList();
        }
    }

    private sealed class RemapHealRunner : SameNamedCloudFolderHealRunner
    {
        private readonly Guid _from;
        private readonly Guid _to;
        private readonly string _path;

        public RemapHealRunner(Guid from, Guid to, string path)
            : base(new CloudApiService(new HttpClient()))
        {
            _from = from;
            _to = to;
            _path = path;
        }

        public int TryHealCount { get; private set; }

        public override Task<(int RemapCount, IReadOnlyList<CloudSyncMapping> Healed)?> TryHealAsync(
            IReadOnlyList<CloudSyncMapping> snapshot,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            TryHealCount++;
            if (userId == Guid.Empty || snapshot.Count == 0)
                return Task.FromResult<(int, IReadOnlyList<CloudSyncMapping>)?>(null);

            var healed = snapshot.Select(m => new CloudSyncMapping
            {
                CloudFolderId = m.CloudFolderId == _from ? _to : m.CloudFolderId,
                LocalPath = string.IsNullOrWhiteSpace(m.LocalPath) ? _path : m.LocalPath,
                CloudFolderName = m.CloudFolderName ?? "MyNotesFolder"
            }).ToList();
            return Task.FromResult<(int, IReadOnlyList<CloudSyncMapping>)?>((1, healed));
        }
    }

    private sealed class HangPushFake : CloudFolderSynchronizer
    {
        public HangPushFake() : base(new CloudApiService(new HttpClient())) { }

        public TaskCompletionSource PushStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePush { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SynchronizeAllCount { get; private set; }
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
                return 0;

            try
            {
                await ReleasePush.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                reportStatus("Upload paused.");
                throw;
            }

            return 0;
        }

        public override Task<bool> SynchronizeAllAsync(
            IEnumerable<CloudSyncMapping> mappings,
            Action<string> reportStatus,
            Action<Exception>? reportFullError,
            Action? onAuthRequired = null,
            CancellationToken cancellationToken = default)
        {
            SynchronizeAllCount++;
            reportStatus("Sync complete.");
            return Task.FromResult(true);
        }
    }
}
