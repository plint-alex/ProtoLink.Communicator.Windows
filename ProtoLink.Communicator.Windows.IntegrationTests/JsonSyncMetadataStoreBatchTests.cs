using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Services.Sync;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Per-Upsert full-file rewrite of multi-MB MyNotesFolder meta never reaches SetLastSyncUtc
/// within daily-use window (1.0.73 sticky Syncing). Batch must coalesce disk writes.
/// </summary>
[Trait("Category", "Offline")]
public class JsonSyncMetadataStoreBatchTests
{
    [Fact]
    public void Batch_ManyUpserts_WritesDiskOnce_AndPersistsLastSync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pl-meta-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "cloud-sync-metadata.json");
        var mappingId = Guid.NewGuid().ToString("N");
        var store = new JsonSyncMetadataStore(dir);

        store.BeginBatch();
        for (var i = 0; i < 200; i++)
        {
            store.Upsert(new SyncItemMeta
            {
                MappingId = mappingId,
                RemoteId = Guid.NewGuid(),
                ParentRemoteId = Guid.NewGuid(),
                RelativePath = $"notes/n{i:D4}.html",
                IsFolder = false,
                SizeBytes = 10,
                ContentHash = "abc",
                RemoteUpdateTime = DateTime.UtcNow
            });
            Assert.False(File.Exists(path), "disk must stay quiet until EndBatch");
            Assert.True(store.IsDirtyForTests);
        }

        var stamped = new DateTime(2026, 11, 17, 1, 2, 3, DateTimeKind.Utc);
        store.SetLastSyncUtc(mappingId, stamped);
        Assert.False(File.Exists(path));
        store.EndBatch();

        Assert.False(store.IsDirtyForTests);
        Assert.True(File.Exists(path));
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(200, doc.RootElement.GetProperty("Items").GetArrayLength());
        Assert.Equal(stamped, doc.RootElement.GetProperty("LastSync").GetProperty(mappingId).GetDateTime());

        try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
    }

    [Fact]
    public void Upsert_OutsideBatch_StillWritesImmediately()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pl-meta-immed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "cloud-sync-metadata.json");
        var store = new JsonSyncMetadataStore(dir);
        store.Upsert(new SyncItemMeta
        {
            MappingId = "m1",
            RemoteId = Guid.NewGuid(),
            ParentRemoteId = Guid.NewGuid(),
            RelativePath = "a.html",
            IsFolder = false,
            SizeBytes = 1,
            ContentHash = "x"
        });
        Assert.True(File.Exists(path));
        try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
    }

    [Fact]
    public void LargeMeta_BatchThousandUpserts_FasterThanPerItemSave()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pl-meta-perf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var mappingId = Guid.NewGuid().ToString("N");

        // Seed ~2k rows so each naive save is expensive (same class as live 27k meta).
        var seed = new JsonSyncMetadataStore(dir);
        seed.BeginBatch();
        for (var i = 0; i < 2000; i++)
        {
            seed.Upsert(new SyncItemMeta
            {
                MappingId = mappingId,
                RemoteId = Guid.NewGuid(),
                ParentRemoteId = Guid.NewGuid(),
                RelativePath = $"seed/s{i:D4}.html",
                IsFolder = false,
                SizeBytes = 8,
                ContentHash = Convert.ToHexString(Encoding.UTF8.GetBytes("h" + i)).ToLowerInvariant()
            });
        }
        seed.EndBatch();

        var naive = new JsonSyncMetadataStore(dir);
        var swNaive = Stopwatch.StartNew();
        for (var i = 0; i < 40; i++)
        {
            naive.Upsert(new SyncItemMeta
            {
                MappingId = mappingId,
                RemoteId = Guid.NewGuid(),
                ParentRemoteId = Guid.NewGuid(),
                RelativePath = $"naive/n{i:D2}.html",
                IsFolder = false,
                SizeBytes = 4,
                ContentHash = "na"
            });
        }
        swNaive.Stop();

        var batched = new JsonSyncMetadataStore(dir);
        var swBatch = Stopwatch.StartNew();
        batched.BeginBatch();
        for (var i = 0; i < 40; i++)
        {
            batched.Upsert(new SyncItemMeta
            {
                MappingId = mappingId,
                RemoteId = Guid.NewGuid(),
                ParentRemoteId = Guid.NewGuid(),
                RelativePath = $"batch/b{i:D2}.html",
                IsFolder = false,
                SizeBytes = 4,
                ContentHash = "ba"
            });
        }
        batched.SetLastSyncUtc(mappingId, DateTime.UtcNow);
        batched.EndBatch();
        swBatch.Stop();

        Assert.True(
            swBatch.ElapsedMilliseconds * 3 < swNaive.ElapsedMilliseconds
            || swBatch.ElapsedMilliseconds < 500,
            $"batch {swBatch.ElapsedMilliseconds}ms should beat naive {swNaive.ElapsedMilliseconds}ms");

        try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
    }

    [Fact]
    public void SnapshotFs_RollsFolderSizesInSinglePass()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-snap-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "a", "b");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "f.txt"), "hello"); // 5 bytes
        File.WriteAllText(Path.Combine(root, "a", "g.txt"), "xy"); // 2 bytes

        var snap = SyncEngine.SnapshotFs(root);
        var folders = snap.Where(e => e.IsFolder).ToDictionary(e => e.RelativePath, e => e.SizeBytes, StringComparer.Ordinal);
        Assert.Equal(5L, folders["a/b"]);
        Assert.Equal(7L, folders["a"]);

        try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
    }
}
