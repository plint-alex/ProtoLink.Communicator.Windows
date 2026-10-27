using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Pure helpers: pick one stable UserContacts bag under CONTACTS+owner.
/// Never create a second bag when any already exists (multi-bag vanish).
/// Dual-parent GetEntities is newest-first — page beyond take so pick sees all siblings.
/// Mirrors ViewEditor <c>user-contacts-bag-helpers.cjs</c> (a2fa5f9).
/// </summary>
public static class UserContactsBagHelpers
{
    /// <summary>Page size for dual-parent UserContacts list (must match ensure fetch take).</summary>
    public const int ListPageSize = 50;

    /// <summary>Hard cap on pages so a broken API cannot loop forever (~25k bags).</summary>
    public const int ListMaxPages = 500;

    /// <summary>Kids page size when enriching person-row counts for pick.</summary>
    public const int KidsTake = 500;

    public sealed class BagSnapshot
    {
        public Guid Id { get; init; }
        public DateTime CreationTime { get; init; }
        public int? PersonRowCount { get; set; }

        public static BagSnapshot FromEntity(GetEntitiesResult entity, int? personRowCount = null) =>
            new()
            {
                Id = entity.Id,
                CreationTime = entity.CreationTime,
                PersonRowCount = personRowCount
            };
    }

    public static bool IsListPageFull(IReadOnlyList<GetEntitiesResult>? page, int pageSize = ListPageSize)
    {
        var take = pageSize > 0 ? pageSize : ListPageSize;
        var len = page?.Count ?? 0;
        return len >= take;
    }

    /// <summary>Merge pages by entity id (later pages do not overwrite earlier).</summary>
    public static List<GetEntitiesResult> MergeBagPages(
        IReadOnlyList<GetEntitiesResult>? existing,
        IReadOnlyList<GetEntitiesResult>? page)
    {
        var outList = existing != null ? new List<GetEntitiesResult>(existing) : new List<GetEntitiesResult>();
        var seen = new HashSet<Guid>();
        foreach (var e in outList)
        {
            if (e.Id != Guid.Empty)
                seen.Add(e.Id);
        }

        if (page == null) return outList;
        foreach (var bag in page)
        {
            if (bag.Id == Guid.Empty || !seen.Add(bag.Id))
                continue;
            outList.Add(bag);
        }

        return outList;
    }

    /// <summary>
    /// Deterministic bag among UserContacts siblings.
    /// Prefer most person rows when any count is known; else oldest creationTime; id tie-break.
    /// </summary>
    public static BagSnapshot? PickBag(IReadOnlyList<BagSnapshot>? bags)
    {
        if (bags == null || bags.Count == 0) return null;
        var list = bags.Where(b => b.Id != Guid.Empty).ToList();
        if (list.Count == 0) return null;
        if (list.Count == 1) return list[0];

        var anyCount = list.Any(b => b.PersonRowCount != null);
        return list
            .OrderByDescending(b => anyCount ? (b.PersonRowCount ?? -1) : 0)
            .ThenBy(b => b.CreationTime == default ? DateTime.MaxValue : b.CreationTime)
            .ThenBy(b => b.Id)
            .First();
    }

    public static Guid? PickBagId(IReadOnlyList<BagSnapshot>? bags) => PickBag(bags)?.Id;

    /// <summary>True only when dual-parent list has no id-bearing bags (safe to AddEntity).</summary>
    public static bool ShouldCreateBag(IReadOnlyList<GetEntitiesResult>? bags)
    {
        if (bags == null) return true;
        foreach (var b in bags)
        {
            if (b.Id != Guid.Empty)
                return false;
        }

        return true;
    }

    public static bool ShouldCreateBag(IReadOnlyList<BagSnapshot>? bags)
    {
        if (bags == null) return true;
        foreach (var b in bags)
        {
            if (b.Id != Guid.Empty)
                return false;
        }

        return true;
    }

    public static List<BagSnapshot> ToSnapshots(IReadOnlyList<GetEntitiesResult>? entities)
    {
        if (entities == null || entities.Count == 0)
            return new List<BagSnapshot>();
        return entities
            .Where(e => e.Id != Guid.Empty)
            .Select(e => BagSnapshot.FromEntity(e))
            .ToList();
    }
}
