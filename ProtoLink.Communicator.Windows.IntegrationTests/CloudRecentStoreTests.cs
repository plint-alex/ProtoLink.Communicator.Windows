using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>C6 Recent durable store — temp-dir roundtrip.</summary>
[Trait("Category", "Offline")]
public class CloudRecentStoreTests
{
    [Fact]
    public void RecordOpen_PersistsAcrossNewStoreInstance()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pl-cloud-recent-" + Guid.NewGuid().ToString("N"));
        try
        {
            var id = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
            var opened = new DateTime(2026, 10, 21, 15, 30, 0, DateTimeKind.Utc);
            var store = new CloudRecentStore(dir);
            store.RecordOpen(new CloudRecentEntry(id, "Docs", true, opened));

            var reloaded = new CloudRecentStore(dir).Load();
            Assert.Single(reloaded);
            Assert.Equal(id, reloaded[0].EntityId);
            Assert.Equal("Docs", reloaded[0].Name);
            Assert.True(reloaded[0].IsFolder);
            Assert.Equal(opened, reloaded[0].OpenedUtc);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void RecordOpen_DedupesAndCaps_NewestFirst()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pl-cloud-recent-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CloudRecentStore(dir);
            var baseTime = new DateTime(2026, 10, 21, 16, 0, 0, DateTimeKind.Utc);
            Guid? oldest = null;
            for (var i = 0; i < CloudRecentList.DefaultCapacity + 2; i++)
            {
                var id = Guid.Parse($"{i:x8}-1111-4111-8111-111111111111");
                if (i == 0) oldest = id;
                store.RecordOpen(new CloudRecentEntry(id, $"n{i}", false, baseTime.AddSeconds(i)));
            }

            var list = store.Load();
            Assert.Equal(CloudRecentList.DefaultCapacity, list.Count);
            Assert.DoesNotContain(list, e => e.EntityId == oldest);

            var bump = list[^1].EntityId;
            store.RecordOpen(new CloudRecentEntry(bump, "bumped", true, baseTime.AddHours(1)));
            list = store.Load();
            Assert.Equal(bump, list[0].EntityId);
            Assert.Equal("bumped", list[0].Name);
            Assert.True(list[0].IsFolder);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Replace_PrunesGoneIds()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pl-cloud-recent-" + Guid.NewGuid().ToString("N"));
        try
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            var store = new CloudRecentStore(dir);
            store.RecordOpen(new CloudRecentEntry(a, "A", true, DateTime.UtcNow));
            store.RecordOpen(new CloudRecentEntry(b, "B", false, DateTime.UtcNow));
            store.Replace(new[] { new CloudRecentEntry(b, "B", false, DateTime.UtcNow) });

            var list = store.Load();
            Assert.Single(list);
            Assert.Equal(b, list[0].EntityId);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
