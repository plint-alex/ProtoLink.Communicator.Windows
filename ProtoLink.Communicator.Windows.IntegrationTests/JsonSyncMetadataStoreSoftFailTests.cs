using System.Text;
using ProtoLink.Communicator.Windows.Services.Sync;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Locked / sharing-violating metadata must soft-fail (empty store), not throw SyncException "Corrupt"
/// — Notes tree share glyphs call Load on cold start (P0 silent MainWindow).
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
    public void Ctor_CorruptJson_ThrowsSyncException()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pl-sync-meta-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "cloud-sync-metadata.json"), "{not-json", Encoding.UTF8);
        try
        {
            var ex = Assert.Throws<SyncException>(() => new JsonSyncMetadataStore(dir));
            Assert.Contains("Corrupt sync metadata", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }
    }
}
