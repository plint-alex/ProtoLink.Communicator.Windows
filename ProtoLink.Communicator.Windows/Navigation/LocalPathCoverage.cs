using System.IO;

namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// Path containment for Notes root ↔ Cloud mapping LocalPath (E6 / AT-MAP-4).
/// Same boundary rule as note↔mapping lookup: equal or nested under a root.
/// </summary>
public static class LocalPathCoverage
{
    public static string? TryNormalizeFull(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return Path.GetFullPath(path.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True when <paramref name="pathFull"/> equals or is nested under <paramref name="rootFull"/>.</summary>
    public static bool IsUnderOrEqual(string pathFull, string rootFull)
    {
        if (string.Equals(pathFull, rootFull, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!pathFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            return false;
        if (pathFull.Length == rootFull.Length)
            return true;
        var next = pathFull[rootFull.Length];
        return next == Path.DirectorySeparatorChar || next == Path.AltDirectorySeparatorChar;
    }

    /// <summary>
    /// Notes root (or any folder) is “in Cloud” when it equals or lies under any mapping local root.
    /// </summary>
    public static bool IsCoveredByAnyMappingRoot(string? folderPath, IEnumerable<string?> mappingLocalRoots)
    {
        var folder = TryNormalizeFull(folderPath);
        if (folder is null) return false;

        foreach (var rootRaw in mappingLocalRoots)
        {
            var root = TryNormalizeFull(rootRaw);
            if (root is null) continue;
            if (IsUnderOrEqual(folder, root))
                return true;
        }

        return false;
    }
}
