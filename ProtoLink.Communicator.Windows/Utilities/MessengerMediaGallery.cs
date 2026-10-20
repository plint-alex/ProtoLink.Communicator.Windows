namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// M9 — build the per-chat media gallery from the already-loaded message list (no second query).
/// Both directions, newest first.
/// </summary>
public static class MessengerMediaGallery
{
    public readonly record struct GalleryEntry(Guid EntityId, DateTime MessageTimestamp, bool IsFromMe);

    /// <summary>
    /// Flatten <c>attachment:</c> ids from messages that are not date separators.
    /// Newest message first; within one message, attachment order is preserved.
    /// Duplicate entity ids keep the newest occurrence only.
    /// </summary>
    public static IReadOnlyList<GalleryEntry> Collect(
        IEnumerable<(IReadOnlyList<Guid> AttachmentIds, DateTime Timestamp, bool IsFromMe, bool IsDateSeparator)> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var ordered = messages
            .Where(m => !m.IsDateSeparator && m.AttachmentIds.Count > 0)
            .OrderByDescending(m => m.Timestamp)
            .ThenByDescending(m => m.IsFromMe);

        var seen = new HashSet<Guid>();
        var result = new List<GalleryEntry>();
        foreach (var m in ordered)
        {
            foreach (var id in m.AttachmentIds)
            {
                if (id == Guid.Empty || !seen.Add(id))
                    continue;
                result.Add(new GalleryEntry(id, m.Timestamp, m.IsFromMe));
            }
        }

        return result;
    }
}
