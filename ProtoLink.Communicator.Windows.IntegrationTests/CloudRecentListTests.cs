using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>C6 Recent MRU helpers — pure, no IO.</summary>
[Trait("Category", "Offline")]
public class CloudRecentListTests
{
    [Fact]
    public void Push_PrependsNewest_AndDedupesById()
    {
        var a = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var b = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var t0 = new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);
        var t1 = t0.AddMinutes(1);
        var t2 = t0.AddMinutes(2);

        var list = CloudRecentList.Push(Array.Empty<CloudRecentEntry>(), new CloudRecentEntry(a, "A", true, t0));
        list = CloudRecentList.Push(list, new CloudRecentEntry(b, "B", false, t1));
        list = CloudRecentList.Push(list, new CloudRecentEntry(a, "A-renamed", true, t2));

        Assert.Equal(2, list.Count);
        Assert.Equal(a, list[0].EntityId);
        Assert.Equal("A-renamed", list[0].Name);
        Assert.Equal(t2, list[0].OpenedUtc);
        Assert.Equal(b, list[1].EntityId);
    }

    [Fact]
    public void Push_CapsAtTwenty_DropsOldest()
    {
        IReadOnlyList<CloudRecentEntry> list = Array.Empty<CloudRecentEntry>();
        var baseTime = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        Guid? first = null;
        for (var i = 0; i < CloudRecentList.DefaultCapacity + 3; i++)
        {
            var id = Guid.Parse($"{i:x8}-0000-4000-8000-000000000000");
            if (i == 0) first = id;
            list = CloudRecentList.Push(list, new CloudRecentEntry(id, $"n{i}", i % 2 == 0, baseTime.AddMinutes(i)));
        }

        Assert.Equal(CloudRecentList.DefaultCapacity, list.Count);
        Assert.DoesNotContain(list, e => e.EntityId == first);
        Assert.Equal(Guid.Parse($"{CloudRecentList.DefaultCapacity + 2:x8}-0000-4000-8000-000000000000"), list[0].EntityId);
    }

    [Fact]
    public void Push_IgnoresVirtualAndEmptyIds()
    {
        var real = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        var list = CloudRecentList.Push(
            Array.Empty<CloudRecentEntry>(),
            new CloudRecentEntry(CloudVirtualNodes.RecentId, "Recent", true, DateTime.UtcNow));
        Assert.Empty(list);

        list = CloudRecentList.Push(list, new CloudRecentEntry(Guid.Empty, "x", false, DateTime.UtcNow));
        Assert.Empty(list);

        list = CloudRecentList.Push(list, new CloudRecentEntry(real, "R", true, DateTime.UtcNow));
        Assert.Single(list);
        Assert.Equal(real, list[0].EntityId);
    }

    [Fact]
    public void KeepOnly_PreservesOrder_AndDropsMissing()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var t = DateTime.UtcNow;
        var list = new[]
        {
            new CloudRecentEntry(a, "A", true, t),
            new CloudRecentEntry(b, "B", false, t),
            new CloudRecentEntry(c, "C", true, t)
        };

        var kept = CloudRecentList.KeepOnly(list, new HashSet<Guid> { a, c });
        Assert.Equal(2, kept.Count);
        Assert.Equal(a, kept[0].EntityId);
        Assert.Equal(c, kept[1].EntityId);
    }

    [Fact]
    public void Remove_DropsMatchingId()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var list = new[]
        {
            new CloudRecentEntry(a, "A", true, DateTime.UtcNow),
            new CloudRecentEntry(b, "B", false, DateTime.UtcNow)
        };
        var after = CloudRecentList.Remove(list, a);
        Assert.Single(after);
        Assert.Equal(b, after[0].EntityId);
    }
}
