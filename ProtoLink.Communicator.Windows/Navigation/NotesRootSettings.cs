using System.IO;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// E11: ordered Notes local roots + last-selected in AppSettings.
/// Migrates legacy single <see cref="AppSettings.NotesRootPath"/> to a one-item list.
/// </summary>
public static class NotesRootSettings
{
    public static bool TryNormalize(string? path, out string full)
    {
        full = string.Empty;
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            full = Path.GetFullPath(path.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return !string.IsNullOrEmpty(full);
        }
        catch
        {
            return false;
        }
    }

    public static bool PathsEqual(string? a, string? b)
    {
        if (!TryNormalize(a, out var na) || !TryNormalize(b, out var nb))
            return false;
        return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ordered unique full paths. Prefers <see cref="AppSettings.NotesRootPaths"/>;
    /// if empty, falls back to legacy <see cref="AppSettings.NotesRootPath"/>.
    /// </summary>
    public static List<string> ResolveRoots(AppSettings settings)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? raw)
        {
            if (!TryNormalize(raw, out var full)) return;
            if (!seen.Add(full)) return;
            result.Add(full);
        }

        if (settings.NotesRootPaths is { Count: > 0 })
        {
            foreach (var p in settings.NotesRootPaths)
                Add(p);
        }
        else
        {
            Add(settings.NotesRootPath);
        }

        return result;
    }

    /// <summary>Last-selected when it is in <paramref name="roots"/>; else legacy path; else first.</summary>
    public static string? ResolveSelected(AppSettings settings, IReadOnlyList<string>? roots = null)
    {
        roots ??= ResolveRoots(settings);
        if (roots.Count == 0) return null;

        if (TryNormalize(settings.LastSelectedNotesRootPath, out var last))
        {
            var match = roots.FirstOrDefault(r => string.Equals(r, last, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        if (TryNormalize(settings.NotesRootPath, out var legacy))
        {
            var match = roots.FirstOrDefault(r => string.Equals(r, legacy, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        return roots[0];
    }

    /// <summary>
    /// Writes multi-root shape and mirrors selected into legacy <see cref="AppSettings.NotesRootPath"/>.
    /// </summary>
    public static void Write(AppSettings settings, IEnumerable<string> roots, string? selected)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in roots)
        {
            if (!TryNormalize(r, out var full)) continue;
            if (!seen.Add(full)) continue;
            list.Add(full);
        }

        string? sel = null;
        if (TryNormalize(selected, out var selFull))
        {
            sel = list.FirstOrDefault(r => string.Equals(r, selFull, StringComparison.OrdinalIgnoreCase));
        }

        if (sel == null && list.Count > 0)
            sel = list[0];

        settings.NotesRootPaths = list;
        settings.LastSelectedNotesRootPath = sel;
        settings.NotesRootPath = sel;
    }

    /// <summary>Neighbor preference after remove: previous index, else next, else null.</summary>
    public static string? PreferNeighborAfterRemove(IReadOnlyList<string> rootsBefore, int removedIndex)
    {
        if (rootsBefore.Count <= 1) return null;
        if (removedIndex < 0 || removedIndex >= rootsBefore.Count) return rootsBefore[0];
        if (removedIndex > 0) return rootsBefore[removedIndex - 1];
        return rootsBefore[1];
    }
}
