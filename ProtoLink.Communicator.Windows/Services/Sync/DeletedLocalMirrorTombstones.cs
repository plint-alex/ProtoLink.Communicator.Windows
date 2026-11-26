using System.Collections.Concurrent;
using System.IO;

namespace ProtoLink.Communicator.Windows.Services.Sync;

/// <summary>
/// Session tombstones for local paths removed by Cloud mapped-folder Delete (AT-CFD-6).
/// Parent maps (e.g. MyNotesFolder) must not SeedInventory / ForcePull rematerialize the
/// same disposable tree after the dedicated map was deleted.
/// </summary>
public static class DeletedLocalMirrorTombstones
{
    private static readonly ConcurrentDictionary<string, byte> Roots =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Remember(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return;
        Roots[Normalize(fullPath)] = 0;
    }

    /// <summary>True when <paramref name="fullPath"/> is the tombstone root or a descendant.</summary>
    public static bool IsUnderTombstone(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return false;
        var full = Normalize(fullPath);
        foreach (var root in Roots.Keys)
        {
            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                return true;
            var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static void ClearForTests() => Roots.Clear();

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }
}
