using System.Text;
using ProtoLink.Communicator.Windows.Services.Sync;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Locked / sharing-violating metadata must soft-fail (empty store), not throw SyncException "Corrupt"
/// — Notes tree share glyphs call Load on cold start (P0 silent MainWindow).
/// Corrupt / null-truncated meta must quarantine and start empty so Push is not stuck forever.
/// </summary>
[Trait("Category", "Offline")]
public class JsonSyncMetadataStoreSoftFailTests
{
    [Fact]
    public void Ctor_WhenFileLockedExclusively_DoesNotThrow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pl-sync-meta-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "cloud-sync-metadata.json");
        File.WriteAllText(path, """{"Items":[],"LastSync":{}}""", Encoding.UTF8);
        try
        {
            using var exclusive = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            var store = new JsonSyncMetadataStore(dir);
            Assert.Empty(store.GetAll("any"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void FirstRead_CorruptJson_QuarantinesAndStartsEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pl-sync-meta-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "cloud-sync-metadata.json");
        File.WriteAllText(path, "{not-json", Encoding.UTF8);
        try
        {
            var store = new JsonSyncMetadataStore(dir);
            Assert.Empty(store.GetAll("any"));
            Assert.False(File.Exists(path), "corrupt live path must be moved aside");
            Assert.NotEmpty(Directory.GetFiles(dir, "cloud-sync-metadata.json.corrupt-*.bak"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void FirstRead_NullByteTruncatedFile_QuarantinesAndStartsEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pl-sync-meta-nul-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "cloud-sync-metadata.json");
        // Live incident: FileMode.Create truncate then crash → multi-MB 0x00.
        File.WriteAllBytes(path, new byte[4096]);
        try
        {
            var store = new JsonSyncMetadataStore(dir);
            Assert.Empty(store.GetAll("any"));
            Assert.False(File.Exists(path));
            Assert.NotEmpty(Directory.GetFiles(dir, "cloud-sync-metadata.json.corrupt-*.bak"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void Save_WritesAtomically_NoStaleTmp_AndValidJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pl-sync-meta-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "cloud-sync-metadata.json");
        try
        {
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
            Assert.False(File.Exists(path + ".tmp"));
            Assert.Equal('{', File.ReadAllText(path, Encoding.UTF8)[0]);
            Assert.Single(new JsonSyncMetadataStore(dir).GetAll("m1"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void Ctor_DoesNotReadFile_Yet()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pl-sync-meta-lazy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "cloud-sync-metadata.json");
        File.WriteAllText(path, "{not-json", Encoding.UTF8);
        try
        {
            // Lazy load: constructing the store must not throw on corrupt/locked files (MainWindow Show path).
            var store = new JsonSyncMetadataStore(dir);
            Assert.NotNull(store);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }
    }
}
