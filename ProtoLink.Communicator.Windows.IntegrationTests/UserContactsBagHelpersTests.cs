using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Pure UC bag pick / stop-spawn / page merge (mirrors ViewEditor helpers).</summary>
[Trait("Category", "Offline")]
public class UserContactsBagHelpersTests
{
    private static readonly Guid Old = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid New = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Mid = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Primary = Guid.Parse("31fc7b5f-0000-4000-8000-000000000001");

    private static UserContactsBagHelpers.BagSnapshot Bag(Guid id, DateTime created, int? count = null) =>
        new() { Id = id, CreationTime = created, PersonRowCount = count };

    private static GetEntitiesResult Entity(Guid id, DateTime? created = null) =>
        new() { Id = id, CreationTime = created ?? default, Code = "UserContacts" };

    [Fact]
    public void Pick_Empty_ReturnsNull_And_ShouldCreate()
    {
        Assert.Null(UserContactsBagHelpers.PickBag(Array.Empty<UserContactsBagHelpers.BagSnapshot>()));
        Assert.Null(UserContactsBagHelpers.PickBag(null));
        Assert.Null(UserContactsBagHelpers.PickBagId(Array.Empty<UserContactsBagHelpers.BagSnapshot>()));
        Assert.True(UserContactsBagHelpers.ShouldCreateBag(Array.Empty<GetEntitiesResult>()));
        Assert.True(UserContactsBagHelpers.ShouldCreateBag((IReadOnlyList<GetEntitiesResult>?)null));
    }

    [Fact]
    public void Pick_Single_ReturnsThatBag_NeverCreate()
    {
        var bag = Bag(Old, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(Old, UserContactsBagHelpers.PickBag(new[] { bag })!.Id);
        Assert.False(UserContactsBagHelpers.ShouldCreateBag(new[] { Entity(Old) }));
    }

    [Fact]
    public void Pick_PreferOldest_WhenNoCounts()
    {
        var bags = new[]
        {
            Bag(New, new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc)),
            Bag(Old, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            Bag(Mid, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)),
        };
        Assert.Equal(Old, UserContactsBagHelpers.PickBagId(bags));
        Assert.False(UserContactsBagHelpers.ShouldCreateBag(bags));
    }

    [Fact]
    public void Pick_PreferMostPersonRows_TinaRichBag()
    {
        var bags = new[]
        {
            Bag(Old, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), 0),
            Bag(Primary, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 5),
            Bag(New, new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), 1),
        };
        Assert.Equal(Primary, UserContactsBagHelpers.PickBagId(bags));
    }

    [Fact]
    public void Pick_CountBeatsAge()
    {
        var bags = new[]
        {
            Bag(Old, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), 0),
            Bag(New, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), 3),
        };
        Assert.Equal(New, UserContactsBagHelpers.PickBagId(bags));
    }

    [Fact]
    public void Pick_IdTieBreak()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var a = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var b = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var bags = new[] { Bag(b, t), Bag(a, t) };
        Assert.Equal(a, UserContactsBagHelpers.PickBagId(bags));
    }

    [Fact]
    public void ShouldCreate_FalseWhenAnyIdBearing()
    {
        Assert.False(UserContactsBagHelpers.ShouldCreateBag(new[] { Entity(New) }));
        Assert.True(UserContactsBagHelpers.ShouldCreateBag(new[] { new GetEntitiesResult() }));
        Assert.False(UserContactsBagHelpers.ShouldCreateBag(new[] { new GetEntitiesResult(), Entity(Old) }));
    }

    [Fact]
    public void IsListPageFull_Truncation()
    {
        var take = UserContactsBagHelpers.ListPageSize;
        Assert.False(UserContactsBagHelpers.IsListPageFull(Array.Empty<GetEntitiesResult>(), take));
        Assert.False(UserContactsBagHelpers.IsListPageFull(null, take));
        Assert.False(UserContactsBagHelpers.IsListPageFull(
            Enumerable.Range(0, take - 1).Select(_ => Entity(Old)).ToList(), take));
        Assert.True(UserContactsBagHelpers.IsListPageFull(
            Enumerable.Range(0, take).Select(_ => Entity(Old)).ToList(), take));
        Assert.True(UserContactsBagHelpers.IsListPageFull(new[] { Entity(Old) }, 1));
    }

    [Fact]
    public void MergePages_DedupesAndKeepsFirst_PagingSeesOldest()
    {
        var page1 = new[]
        {
            Entity(New, new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc)),
            Entity(Mid, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)),
        };
        var page2 = new[]
        {
            Entity(New, new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc)),
            Entity(Old, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
        };
        var merged = UserContactsBagHelpers.MergeBagPages(page1, page2);
        Assert.Equal(3, merged.Count);
        Assert.Equal(New, merged[0].Id);
        var snaps = UserContactsBagHelpers.ToSnapshots(merged);
        Assert.Equal(Old, UserContactsBagHelpers.PickBagId(snaps));
        Assert.False(UserContactsBagHelpers.ShouldCreateBag(merged));
    }

    [Fact]
    public void TruncationHidesOldest_WithoutPaging()
    {
        const int take = 2;
        var newestFirst = new[]
        {
            Entity(New, new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc)),
            Entity(Mid, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)),
            Entity(Old, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
        };
        var firstPage = newestFirst.Take(take).ToList();
        Assert.True(UserContactsBagHelpers.IsListPageFull(firstPage, take));
        Assert.Equal(Mid, UserContactsBagHelpers.PickBagId(UserContactsBagHelpers.ToSnapshots(firstPage)));
        var all = UserContactsBagHelpers.MergeBagPages(firstPage, newestFirst.Skip(take).ToList());
        Assert.False(UserContactsBagHelpers.IsListPageFull(newestFirst.Skip(take).ToList(), take));
        Assert.Equal(Old, UserContactsBagHelpers.PickBagId(UserContactsBagHelpers.ToSnapshots(all)));
    }
}
