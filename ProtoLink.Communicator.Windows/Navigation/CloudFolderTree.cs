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
        Core.CloudEntityClassify.IsFolder(typeEntityId: null, code);

    /// <summary>Folder-tree row when TypeEntityId and/or legacy Code is available.</summary>
    public static bool IsFolderTreeEntity(Guid? typeEntityId, string? code) =>
        Core.CloudEntityClassify.IsFolder(typeEntityId, code);

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

    /// <summary>
    /// Listing-catch first paint: prefer breadcrumb tree root; else My Cloud root; else listed folder.
    /// Used when GetEntities soft-fails before <c>RefreshFolderTreeAsync</c> can run.
    /// </summary>
    public static Guid? ResolveListingSoftFailStubRootId(
        IReadOnlyList<(Guid Id, string? Code)> crumbs,
        Guid? photosLibraryId,
        Guid? listedFolderId,
        Guid myCloudRootId,
        Func<Guid, bool> isVirtual)
    {
        var fromCrumbs = PickTreeRootId(crumbs, photosLibraryId, isVirtual);
        if (fromCrumbs is { } crumbRoot && crumbRoot != Guid.Empty)
            return crumbRoot;

        if (myCloudRootId != Guid.Empty
            && (listedFolderId is null || !isVirtual(listedFolderId.Value)))
            return myCloudRootId;

        if (listedFolderId is { } listed && listed != Guid.Empty && !isVirtual(listed))
            return listed;

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

    /// <summary>
    /// Mid-session listing refresh: keep prior expands when the tree root is unchanged.
    /// First paint / new root → only the new root (E9b collapse-from-start).
    /// </summary>
    public static HashSet<Guid> ExpandSetForRefresh(
        Guid? previousRootId,
        Guid newRootId,
        IReadOnlyCollection<Guid>? previousExpanded)
    {
        if (newRootId == Guid.Empty)
            return new HashSet<Guid>();

        if (previousRootId == newRootId && previousExpanded is { Count: > 0 })
        {
            var kept = new HashSet<Guid>(previousExpanded);
            kept.Add(newRootId);
            return kept;
        }

        return new HashSet<Guid> { newRootId };
    }

    /// <summary>
    /// Same-root mid-session refresh must keep prior children/names in the cache so
    /// <see cref="ProjectVisible"/> stays populated while Loaded is cleared and Ensure re-fetches.
    /// Clearing children first (1.0.102) made any mid-await Rebuild paint root-only wipe.
    /// </summary>
    public static bool KeepChildrenAcrossRefresh(Guid? previousRootId, Guid newRootId) =>
        previousRootId is { } prev && prev != Guid.Empty && prev == newRootId;

    /// <summary>
    /// Union a refresh baseline with expands committed while refresh awaited (lazy Toggle race).
    /// Same-root mid-session must not drop live expands that landed after the baseline snapshot.
    /// </summary>
    public static HashSet<Guid> UnionExpandSets(
        IReadOnlyCollection<Guid>? baseline,
        IReadOnlyCollection<Guid>? live,
        Guid rootId)
    {
        var set = new HashSet<Guid>();
        if (baseline != null)
        {
            foreach (var id in baseline)
                set.Add(id);
        }

        if (live != null)
        {
            foreach (var id in live)
                set.Add(id);
        }

        if (rootId != Guid.Empty)
            set.Add(rootId);
        return set;
    }

    /// <summary>
    /// After listing IncludeValues hydrate (or mapping Name resolve): replace synthetic
    /// <c>folder-{id}</c> labels in the values-light tree cache so Folders tree catches up
    /// without a wipe. Only updates ids already present in names or children.
    /// </summary>
    public static bool ApplyHumanNames(
        IDictionary<Guid, string> namesById,
        IDictionary<Guid, IReadOnlyList<Node>> childrenByParent,
        IEnumerable<(Guid Id, string Name)> humanNames)
    {
        if (namesById == null || childrenByParent == null || humanNames == null)
            return false;

        var map = new Dictionary<Guid, string>();
        foreach (var (id, name) in humanNames)
        {
            if (id == Guid.Empty || string.IsNullOrWhiteSpace(name)) continue;
            map[id] = name.Trim();
        }

        if (map.Count == 0) return false;

        var changed = false;
        foreach (var (id, name) in map)
        {
            var inNames = namesById.ContainsKey(id);
            var inChildren = false;
            if (!inNames)
            {
                foreach (var kids in childrenByParent.Values)
                {
                    if (kids == null) continue;
                    foreach (var k in kids)
                    {
                        if (k.Id != id) continue;
                        inChildren = true;
                        break;
                    }

                    if (inChildren) break;
                }
            }

            if (!inNames && !inChildren) continue;
            if (namesById.TryGetValue(id, out var cur) && string.Equals(cur, name, StringComparison.Ordinal))
                continue;
            namesById[id] = name;
            changed = true;
        }

        if (!changed) return false;

        foreach (var parentId in childrenByParent.Keys.ToList())
        {
            var kids = childrenByParent[parentId];
            if (kids == null || kids.Count == 0) continue;
            List<Node>? next = null;
            for (var i = 0; i < kids.Count; i++)
            {
                var k = kids[i];
                if (!map.TryGetValue(k.Id, out var n) || string.Equals(k.Name, n, StringComparison.Ordinal))
                    continue;
                next ??= kids.ToList();
                next[i] = k with { Name = n };
            }

            if (next != null)
                childrenByParent[parentId] = next;
        }

        return true;
    }
}
