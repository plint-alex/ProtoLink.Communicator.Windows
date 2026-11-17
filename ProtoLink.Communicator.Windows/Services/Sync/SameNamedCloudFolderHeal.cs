namespace ProtoLink.Communicator.Windows.Services.Sync;

/// <summary>
/// Dual same-named CloudFolders (e.g. two "MyNotesFolder") break cross-device sync when
/// Create-new in the map wizard adds a second id while another client already maps the populated one.
/// Canonical pick among case-insensitive name matches: higher ImmediateChildCount, then earliest
/// CreationTime, then Id — mirrors Android <c>SameNamedCloudFolderHeal</c>.
/// </summary>
public static class SameNamedCloudFolderHeal
{
    public readonly record struct Sibling(
        Guid Id,
        string Name,
        string? CreationTime = null,
        int ImmediateChildCount = 0);

    public readonly record struct MappingRow(
        Guid CloudFolderId,
        string LocalPath,
        string CloudFolderName);

    public readonly record struct Remap(
        Guid FromId,
        Guid ToId,
        string LocalPath,
        string FolderName);

    /// <summary>Prefer existing same-named sibling instead of AddEntity(create). Populated, then oldest.</summary>
    public static Sibling? PreferExistingSameName(string desiredName, IReadOnlyList<Sibling> siblings)
    {
        var name = desiredName.Trim();
        if (name.Length == 0) return null;
        var matches = siblings
            .Where(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0) return null;
        return matches.OrderBy(s => s, SiblingCanonicalComparer.Instance).First();
    }

    /// <summary>
    /// Among siblings sharing <paramref name="mappedName"/>, return the canonical id.
    /// Null when there is no duplicate or mapped already is canonical.
    /// </summary>
    public static Guid? CanonicalIdIfMappedIsDuplicate(
        Guid mappedId,
        string mappedName,
        IReadOnlyList<Sibling> siblings)
    {
        var name = mappedName.Trim();
        if (name.Length == 0 || mappedId == Guid.Empty) return null;
        var matches = siblings
            .Where(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count < 2) return null;
        var canonical = matches.OrderBy(s => s, SiblingCanonicalComparer.Instance).First();
        if (canonical.Id == mappedId) return null;
        return canonical.Id;
    }

    /// <summary>
    /// For each mapping whose Cloud folder name collides with a sibling under the same parent,
    /// remap localPath onto the canonical (populated, then oldest) id.
    /// </summary>
    public static IReadOnlyList<Remap> RemapsForDuplicateNames(
        IReadOnlyList<MappingRow> mappings,
        IReadOnlyDictionary<Guid, IReadOnlyList<Sibling>> siblingsByParent,
        IReadOnlyDictionary<Guid, Guid> parentIdByMappedId)
    {
        var outList = new List<Remap>();
        var seenLocal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mappings)
        {
            if (!parentIdByMappedId.TryGetValue(m.CloudFolderId, out var parent))
                continue;
            if (!siblingsByParent.TryGetValue(parent, out var siblings))
                continue;
            var name = string.IsNullOrWhiteSpace(m.CloudFolderName)
                ? siblings.FirstOrDefault(s => s.Id == m.CloudFolderId).Name ?? ""
                : m.CloudFolderName;
            var toId = CanonicalIdIfMappedIsDuplicate(m.CloudFolderId, name, siblings);
            if (toId is null) continue;
            var localKey = m.LocalPath.Trim();
            if (localKey.Length > 0 && !seenLocal.Add(localKey)) continue;
            var toName = siblings.FirstOrDefault(s => s.Id == toId.Value).Name;
            if (string.IsNullOrWhiteSpace(toName)) toName = name;
            outList.Add(new Remap(m.CloudFolderId, toId.Value, m.LocalPath, toName));
        }

        return outList;
    }

    /// <summary>Apply remaps: drop from/to ids and any same localPath, then add canonical rows.</summary>
    public static IReadOnlyList<MappingRow> ApplyRemaps(
        IReadOnlyList<MappingRow> mappings,
        IReadOnlyList<Remap> remaps)
    {
        if (remaps.Count == 0) return mappings;
        var dropIds = remaps
            .SelectMany(r => new[] { r.FromId, r.ToId })
            .ToHashSet();
        var dropLocals = remaps
            .Select(r => r.LocalPath.Trim())
            .Where(p => p.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = mappings.Where(m =>
        {
            if (dropIds.Contains(m.CloudFolderId)) return false;
            var local = m.LocalPath.Trim();
            return local.Length == 0 || !dropLocals.Contains(local);
        });
        var added = remaps.Select(r => new MappingRow(r.ToId, r.LocalPath, r.FolderName));
        return kept.Concat(added).ToList();
    }

    /// <summary>When duplicate names exist in a pick list, show short id so the user can tell them apart.</summary>
    public static string DisplayLabel(string name, Guid id, IEnumerable<string> allNames)
    {
        var n = string.IsNullOrWhiteSpace(name) ? id.ToString("N")[..8] : name;
        var dup = allNames.Count(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)) > 1;
        return dup && id != Guid.Empty ? $"{n} · {id.ToString("N")[..8]}" : n;
    }

    /// <summary>Dedupe siblings by id; keep richer childCount / name / creationTime.</summary>
    public static IReadOnlyList<Sibling> MergeById(params IEnumerable<Sibling>[] lists)
    {
        var map = new Dictionary<Guid, Sibling>();
        foreach (var list in lists)
        {
            foreach (var s in list)
            {
                if (s.Id == Guid.Empty) continue;
                if (!map.TryGetValue(s.Id, out var prev))
                {
                    map[s.Id] = s;
                    continue;
                }

                map[s.Id] = prev with
                {
                    Name = string.IsNullOrWhiteSpace(prev.Name) ? s.Name : prev.Name,
                    CreationTime = string.IsNullOrWhiteSpace(prev.CreationTime)
                        ? s.CreationTime
                        : prev.CreationTime,
                    ImmediateChildCount = Math.Max(prev.ImmediateChildCount, s.ImmediateChildCount)
                };
            }
        }

        return map.Values.ToList();
    }

    /// <summary>
    /// Pick CloudRoot ids to scan for cross-root same-named duplicates.
    /// Always includes <paramref name="mustIncludeIds"/>, then "Drive", then oldest roots — capped by
    /// <paramref name="maxRoots"/>.
    /// </summary>
    public static IReadOnlyList<Guid> CloudRootIdsForDiscovery(
        IReadOnlyList<Sibling> cloudRoots,
        IEnumerable<Guid> mustIncludeIds,
        int maxRoots = 16)
    {
        if (maxRoots <= 0) return Array.Empty<Guid>();
        var outList = new List<Guid>();
        var seen = new HashSet<Guid>();

        void Add(Guid id)
        {
            if (id == Guid.Empty || !seen.Add(id)) return;
            outList.Add(id);
        }

        foreach (var id in mustIncludeIds)
            Add(id);
        foreach (var r in cloudRoots)
        {
            if (string.Equals(r.Name, "Drive", StringComparison.OrdinalIgnoreCase))
                Add(r.Id);
        }

        var oldestFirst = cloudRoots
            .OrderBy(r =>
            {
                var t = r.CreationTime?.Trim();
                return string.IsNullOrEmpty(t) ? "\uFFFF" : t;
            })
            .ThenBy(r => r.Id);
        foreach (var r in oldestFirst)
        {
            if (outList.Count >= maxRoots) break;
            Add(r.Id);
        }

        if (outList.Count > maxRoots)
            return outList.Take(maxRoots).ToList();
        return outList;
    }

    private sealed class SiblingCanonicalComparer : IComparer<Sibling>
    {
        public static readonly SiblingCanonicalComparer Instance = new();

        public int Compare(Sibling a, Sibling b)
        {
            var childCmp = b.ImmediateChildCount.CompareTo(a.ImmediateChildCount);
            if (childCmp != 0) return childCmp;
            var ta = a.CreationTime?.Trim() ?? "";
            var tb = b.CreationTime?.Trim() ?? "";
            if (ta.Length > 0 && tb.Length > 0 && ta != tb)
                return string.CompareOrdinal(ta, tb);
            if (ta.Length > 0 && tb.Length == 0) return -1;
            if (ta.Length == 0 && tb.Length > 0) return 1;
            return a.Id.CompareTo(b.Id);
        }
    }
}
