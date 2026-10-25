namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>Pure helpers for Communicator Cloud/Photos persistent folder tree (AT-PC1 / AT-PC8).</summary>
public static class CloudFolderTree
{
    public const int DefaultMaxFolders = 200;
    public const int DefaultMaxDepth = 12;

    public sealed record Node(Guid Id, string Name);

    public sealed record FlatRow(Guid Id, string Name, int Depth, bool Selected);

    public static bool IsFolderTreeCode(string? code) =>
        code is Core.CloudEntityCodes.CloudFolder or Core.CloudEntityCodes.CloudRoot;

    /// <summary>
    /// Tree root from breadcrumb: prefer CloudRoot id when present; else first non-virtual crumb;
    /// when <paramref name="photosLibraryId"/> is on the trail, prefer that (Photos chrome).
    /// </summary>
    public static Guid? PickTreeRootId(
        IReadOnlyList<(Guid Id, string? Code)> crumbs,
        Guid? photosLibraryId,
        Func<Guid, bool> isVirtual)
    {
        if (crumbs == null || crumbs.Count == 0) return null;

        if (photosLibraryId is { } lib && lib != Guid.Empty)
        {
            foreach (var c in crumbs)
            {
                if (c.Id == lib) return lib;
            }
        }

        foreach (var c in crumbs)
        {
            if (isVirtual(c.Id)) continue;
            if (string.Equals(c.Code, Core.CloudEntityCodes.CloudRoot, StringComparison.Ordinal))
                return c.Id;
        }

        foreach (var c in crumbs)
        {
            if (!isVirtual(c.Id)) return c.Id;
        }

        return null;
    }

    /// <summary>Highlight id: file mode uses containing folder; folder uses open entity.</summary>
    public static Guid? SelectedFolderId(bool isFileOpen, Guid? openEntityId, Guid? parentFolderId) =>
        isFileOpen ? parentFolderId : openEntityId;

    public static IReadOnlyList<FlatRow> Flatten(
        Node? root,
        IReadOnlyDictionary<Guid, IReadOnlyList<Node>> childrenByParent,
        Guid? selectedId,
        int maxDepth = DefaultMaxDepth)
    {
        if (root == null || root.Id == Guid.Empty) return Array.Empty<FlatRow>();
        var outRows = new List<FlatRow>();
        Walk(root.Id, root.Name, 0);
        return outRows;

        void Walk(Guid id, string name, int depth)
        {
            if (depth > maxDepth) return;
            outRows.Add(new FlatRow(id, name, depth, selectedId is { } s && s == id));
            if (depth >= maxDepth) return;
            if (!childrenByParent.TryGetValue(id, out var kids) || kids == null) return;
            foreach (var k in kids)
                Walk(k.Id, k.Name, depth + 1);
        }
    }
}
