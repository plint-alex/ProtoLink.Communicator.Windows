namespace ProtoLink.Communicator.Windows.Services.Sync;

/// <summary>
/// Before Sync AddEntity/AddFile, reuse an existing CloudFile/CloudFolder sibling under the
/// same parent with the same display name (Cloud can hold same-name siblings; local FS cannot).
/// Prefer newest UpdateTime, then Id — matches devops cleanup keep-newest for files.
/// </summary>
public static class SameNamedCloudSibling
{
    public static RemoteEntryInfo? PreferExisting(
        Guid parentId,
        string displayName,
        bool isFolder,
        IEnumerable<RemoteEntryInfo> siblings)
    {
        var name = displayName.Trim();
        if (name.Length == 0 || parentId == Guid.Empty)
            return null;

        RemoteEntryInfo? best = null;
        foreach (var s in siblings)
        {
            if (s.ParentId != parentId) continue;
            if (s.IsFolder != isFolder) continue;
            if (string.IsNullOrWhiteSpace(s.Name)) continue;
            if (!string.Equals(s.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))
                continue;
            // Skip synthetic file-{guid} labels — not a real display-name match.
            if (!isFolder &&
                string.Equals(s.Name.Trim(), $"file-{s.Id:N}", StringComparison.OrdinalIgnoreCase))
                continue;
            if (best == null || ComparePrefer(s, best) < 0)
                best = s;
        }

        return best;
    }

    /// <summary>Newer UpdateTime wins; then smaller Id. Returns &lt;0 when a is preferred over b.</summary>
    private static int ComparePrefer(RemoteEntryInfo a, RemoteEntryInfo b)
    {
        var aTime = a.UpdateTime ?? DateTime.MinValue;
        var bTime = b.UpdateTime ?? DateTime.MinValue;
        var byTime = bTime.CompareTo(aTime); // newer first
        if (byTime != 0) return byTime;
        return a.Id.CompareTo(b.Id);
    }
}
