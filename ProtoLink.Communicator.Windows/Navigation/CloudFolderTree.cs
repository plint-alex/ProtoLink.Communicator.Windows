namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>Pure helpers for Communicator Cloud/Photos persistent folder tree (AT-PC1 / AT-PC8 / E9b).</summary>
public static class CloudFolderTree
{
    public const int DefaultMaxFolders = 200;
    public const int DefaultMaxDepth = 12;

    public sealed record Node(Guid Id, string Name);

    public sealed record FlatRow(Guid Id, string Name, int Depth, bool Selected);

    /// <summary>Visible tree row with expand state (E9b / AT-CE9…CE11).</summary>
    public sealed record VisibleRow(
        Guid Id,
        string Name,
        int Depth,
        bool Selected,
        bool IsExpanded,
        bool CanExpand,
        bool IsLoading);

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

    /// <summary>
    /// Full DFS flatten (legacy / tests). Prefer <see cref="ProjectVisible"/> for E9b UI.
    /// </summary>
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

    /// <summary>
    /// Project only expanded paths. Unloaded parents stay expandable (lazy). Loaded empty parents are leaves.
    /// </summary>
    public static IReadOnlyList<VisibleRow> ProjectVisible(
        Node? root,
        IReadOnlyDictionary<Guid, IReadOnlyList<Node>> childrenByParent,
        IReadOnlySet<Guid> expandedIds,
        IReadOnlySet<Guid> loadedParentIds,
        Guid? selectedId,
        IReadOnlySet<Guid>? loadingIds = null,
        int maxDepth = DefaultMaxDepth)
    {
        if (root == null || root.Id == Guid.Empty) return Array.Empty<VisibleRow>();
        expandedIds ??= new HashSet<Guid>();
        loadedParentIds ??= new HashSet<Guid>();
        loadingIds ??= new HashSet<Guid>();
        var outRows = new List<VisibleRow>();
        Walk(root.Id, root.Name, 0);
        return outRows;

        void Walk(Guid id, string name, int depth)
        {
            if (depth > maxDepth) return;
            var loaded = loadedParentIds.Contains(id);
            IReadOnlyList<Node>? kids = null;
            var hasKids = false;
            if (childrenByParent.TryGetValue(id, out var listed) && listed != null)
            {
                kids = listed;
                hasKids = listed.Count > 0;
            }

            var canExpand = !loaded || hasKids;
            var isExpanded = canExpand && expandedIds.Contains(id);
            var isLoading = loadingIds.Contains(id);
            outRows.Add(new VisibleRow(
                id,
                name,
                depth,
                selectedId is { } s && s == id,
                isExpanded,
                canExpand,
                isLoading));

            if (!isExpanded || depth >= maxDepth || kids == null) return;
            foreach (var k in kids)
                Walk(k.Id, k.Name, depth + 1);
        }
    }

    /// <summary>Remove <paramref name="folderId"/> and all known descendants from the expanded set.</summary>
    public static void CollapseBranch(
        Guid folderId,
        IReadOnlyDictionary<Guid, IReadOnlyList<Node>> childrenByParent,
        ISet<Guid> expandedIds)
    {
        if (!expandedIds.Remove(folderId)) return;
        if (!childrenByParent.TryGetValue(folderId, out var kids) || kids == null) return;
        foreach (var k in kids)
            CollapseBranch(k.Id, childrenByParent, expandedIds);
    }
}
