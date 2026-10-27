using System.IO;
using System.Windows.Threading;

namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Cloud folder change-then-wait helpers (#cloud-folder-responsiveness / #ui-change-then-wait).
/// Chrome/selection must update before GetEntities, sync-meta, or local-tree size walks.
/// </summary>
public static class CloudFolderNavigation
{
    /// <summary>Same priority as <see cref="ShellTabActivation.AfterChromePaint"/>.</summary>
    public static DispatcherPriority AfterChromePaint { get; } = ShellTabActivation.AfterChromePaint;

    /// <summary>
    /// Walk flat tree rows (depth order) back from <paramref name="selectedId"/> to the tree root.
    /// Empty when the id is not in the rows.
    /// </summary>
    public static IReadOnlyList<(Guid Id, string Name)> BuildAncestorTrail(
        IReadOnlyList<(Guid Id, string Name, int Depth)> rows,
        Guid selectedId)
    {
        if (rows == null || rows.Count == 0) return Array.Empty<(Guid, string)>();

        var idx = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Id == selectedId)
            {
                idx = i;
                break;
            }
        }

        if (idx < 0) return Array.Empty<(Guid, string)>();

        var trail = new List<(Guid Id, string Name)>();
        var depth = rows[idx].Depth;
        trail.Add((rows[idx].Id, rows[idx].Name));
        for (var i = idx - 1; i >= 0 && depth > 0; i--)
        {
            if (rows[i].Depth != depth - 1) continue;
            trail.Insert(0, (rows[i].Id, rows[i].Name));
            depth--;
        }

        return trail;
    }

    /// <summary>
    /// Prefix existing breadcrumb crumbs that sit above the tree root, then append the tree trail
    /// (which starts at the tree root).
    /// </summary>
    public static IReadOnlyList<(Guid Id, string Name)> ComposeBreadcrumb(
        IReadOnlyList<(Guid Id, string Name)> existingCrumbs,
        IReadOnlyList<(Guid Id, string Name)> treeAncestorsToSelected)
    {
        if (treeAncestorsToSelected == null || treeAncestorsToSelected.Count == 0)
            return existingCrumbs?.ToList() ?? new List<(Guid, string)>();

        var treeRoot = treeAncestorsToSelected[0].Id;
        var prefix = new List<(Guid Id, string Name)>();
        if (existingCrumbs != null)
        {
            foreach (var c in existingCrumbs)
            {
                if (c.Id == treeRoot) break;
                prefix.Add(c);
            }
        }

        prefix.AddRange(treeAncestorsToSelected);
        return prefix;
    }

    /// <summary>
    /// Sum bytes under a mapped local folder. Must not run on the UI thread for large trees.
    /// </summary>
    public static long SumLocalBytes(string localPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        return Directory.EnumerateFiles(localPath, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
    }
}
