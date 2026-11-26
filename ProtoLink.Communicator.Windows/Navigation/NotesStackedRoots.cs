using System.IO;

namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// E11a: pure helpers for Windows Notes stacked local roots (last-open page/root, containing root).
/// </summary>
public static class NotesStackedRoots
{
    /// <summary>
    /// Longest configured root that contains <paramref name="path"/> (path equals root or is under it).
    /// </summary>
    public static string? FindContainingRoot(IEnumerable<string> roots, string? path)
    {
        if (!NotesRootSettings.TryNormalize(path, out var full))
            return null;

        string? best = null;
        var bestLen = -1;
        foreach (var raw in roots)
        {
            if (!NotesRootSettings.TryNormalize(raw, out var root))
                continue;
            if (!IsUnderOrEqual(root, full))
                continue;
            if (root.Length > bestLen)
            {
                best = root;
                bestLen = root.Length;
            }
        }

        return best;
    }

    public static bool IsUnderOrEqual(string rootFull, string pathFull)
    {
        if (!NotesRootSettings.TryNormalize(rootFull, out var root)
            || !NotesRootSettings.TryNormalize(pathFull, out var path))
            return false;
        if (string.Equals(root, path, StringComparison.OrdinalIgnoreCase))
            return true;
        var prefix = root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Last-open page when it still exists under a configured root; otherwise null.
    /// </summary>
    public static string? ResolveLastOpenPage(IReadOnlyList<string> roots, string? lastOpenPagePath)
    {
        if (!NotesRootSettings.TryNormalize(lastOpenPagePath, out var page))
            return null;
        if (FindContainingRoot(roots, page) == null)
            return null;
        try
        {
            return Directory.Exists(page) ? page : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True when every existing (on-disk) root has zero child folders.</summary>
    public static bool AllExistingRootsEmpty(IEnumerable<(string Path, int ChildCount, bool Exists)> roots) =>
        roots.Any(r => r.Exists) && roots.Where(r => r.Exists).All(r => r.ChildCount == 0);
}
