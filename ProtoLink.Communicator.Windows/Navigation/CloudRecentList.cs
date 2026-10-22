using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// Pure MRU helpers for Cloud Recent (C6): dedupe by entity id, newest first, capped.
/// </summary>
public static class CloudRecentList
{
    public const int DefaultCapacity = 20;

    /// <summary>
    /// Prepend <paramref name="entry"/> (or move existing id to front with refreshed fields), then cap.
    /// Virtual / empty ids are ignored.
    /// </summary>
    public static IReadOnlyList<CloudRecentEntry> Push(
        IReadOnlyList<CloudRecentEntry> current,
        CloudRecentEntry entry,
        int capacity = DefaultCapacity)
    {
        if (capacity < 1) capacity = DefaultCapacity;
        if (entry.EntityId == Guid.Empty || CloudVirtualNodes.IsVirtual(entry.EntityId))
            return current;

        var opened = entry.OpenedUtc.Kind == DateTimeKind.Utc
            ? entry.OpenedUtc
            : entry.OpenedUtc.ToUniversalTime();
        var name = string.IsNullOrWhiteSpace(entry.Name) ? entry.EntityId.ToString("D") : entry.Name.Trim();
        var next = new CloudRecentEntry(entry.EntityId, name, entry.IsFolder, opened);

        var result = new List<CloudRecentEntry>(capacity + 1) { next };
        foreach (var existing in current)
        {
            if (existing.EntityId == next.EntityId) continue;
            result.Add(existing);
            if (result.Count >= capacity) break;
        }

        return result;
    }

    public static IReadOnlyList<CloudRecentEntry> Remove(
        IReadOnlyList<CloudRecentEntry> current,
        Guid entityId)
    {
        if (entityId == Guid.Empty) return current;
        var result = current.Where(e => e.EntityId != entityId).ToList();
        return result.Count == current.Count ? current : result;
    }

    /// <summary>Keep only entries whose ids are in <paramref name="keepIds"/> (preserve MRU order).</summary>
    public static IReadOnlyList<CloudRecentEntry> KeepOnly(
        IReadOnlyList<CloudRecentEntry> current,
        IReadOnlySet<Guid> keepIds)
    {
        var result = current.Where(e => keepIds.Contains(e.EntityId)).ToList();
        return result.Count == current.Count ? current : result;
    }
}
