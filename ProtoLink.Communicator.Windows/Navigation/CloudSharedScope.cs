using System.Linq;

namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// Shared-with-me navigation helpers (AT-S42 refined): Map allowed only when the active share root is writable.
/// </summary>
public static class CloudSharedScope
{
    public static bool IsInSharedWithMeScope(IEnumerable<Guid> breadcrumbIds) =>
        breadcrumbIds.Any(id => id == CloudVirtualNodes.SharedWithMeId);

    /// <summary>
    /// First breadcrumb id after the Shared-with-me virtual node, or null when still on the listing / not in scope.
    /// </summary>
    public static Guid? TryGetActiveShareRootId(IEnumerable<Guid> breadcrumbIds)
    {
        var seenShared = false;
        foreach (var id in breadcrumbIds)
        {
            if (!seenShared)
            {
                if (id == CloudVirtualNodes.SharedWithMeId)
                    seenShared = true;
                continue;
            }

            return id;
        }

        return null;
    }

    /// <summary>
    /// Whether the share root that owns <paramref name="folderId"/> (or the active breadcrumb root) has CanWrite.
    /// When still on the Shared-with-me listing, <paramref name="folderId"/> itself is treated as the share root.
    /// </summary>
    public static bool IsShareRootWritable(
        IEnumerable<Guid> breadcrumbIds,
        Guid folderId,
        IReadOnlyDictionary<Guid, bool> shareRootCanWrite)
    {
        var rootId = TryGetActiveShareRootId(breadcrumbIds) ?? folderId;
        return shareRootCanWrite.TryGetValue(rootId, out var canWrite) && canWrite;
    }

    /// <summary>Outside Shared with me always true; inside, only when the active share root is writable.</summary>
    public static bool AllowsMapUnderSharedScope(
        IEnumerable<Guid> breadcrumbIds,
        Guid folderId,
        IReadOnlyDictionary<Guid, bool> shareRootCanWrite) =>
        !IsInSharedWithMeScope(breadcrumbIds)
        || IsShareRootWritable(breadcrumbIds, folderId, shareRootCanWrite);

    public static bool ShowSharedBrowseOnlyHint(
        IEnumerable<Guid> breadcrumbIds,
        Guid? currentFolderId,
        bool currentIsFolder,
        IReadOnlyDictionary<Guid, bool> shareRootCanWrite)
    {
        if (currentFolderId is null || !currentIsFolder) return false;
        if (CloudVirtualNodes.IsVirtual(currentFolderId.Value)) return false;
        if (!IsInSharedWithMeScope(breadcrumbIds)) return false;
        return !IsShareRootWritable(breadcrumbIds, currentFolderId.Value, shareRootCanWrite);
    }

    public static bool CanMapCurrentFolder(
        Guid? currentFolderId,
        IEnumerable<Guid> breadcrumbIds,
        IReadOnlySet<Guid> mappedFolderIds,
        IReadOnlyDictionary<Guid, bool> shareRootCanWrite)
    {
        if (currentFolderId is null) return false;
        if (CloudVirtualNodes.IsVirtual(currentFolderId.Value)) return false;
        if (!AllowsMapUnderSharedScope(breadcrumbIds, currentFolderId.Value, shareRootCanWrite))
            return false;
        if (mappedFolderIds.Contains(currentFolderId.Value)) return false;
        if (breadcrumbIds.Any(mappedFolderIds.Contains)) return false;
        return true;
    }

    /// <summary>
    /// UX-LS-1: Leave on open share-root header when the open entity is the active Shared-with-me root
    /// (direct personal grant known from the SharedWithMe listing cache).
    /// </summary>
    public static bool CanLeaveOpenShareRoot(
        Guid? currentEntityId,
        IEnumerable<Guid> breadcrumbIds,
        IReadOnlyDictionary<Guid, bool> knownShareRoots)
    {
        if (currentEntityId is null) return false;
        var root = TryGetActiveShareRootId(breadcrumbIds);
        return root == currentEntityId && knownShareRoots.ContainsKey(root.Value);
    }

    /// <summary>UX-LS-1: Leave on Shared-with-me list rows only (share roots from S6).</summary>
    public static bool CanLeaveSharedListRow(bool isSharedWithMeListing, bool isShareRoot, bool isVirtualEntry) =>
        isSharedWithMeListing && isShareRoot && !isVirtualEntry;
}
