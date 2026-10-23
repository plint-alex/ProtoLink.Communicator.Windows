using System.IO;

namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// Post-delete Notes navigation: previous sibling → parent → notes root.
/// Spec: docs/features/entity-navigation/notes-delete-navigate (AT-ND-1…4).
/// Call <see cref="ResolveNextPath"/> before deleting so siblings still list the removed folder.
/// </summary>
public static class NotesDeleteNavigate
{
    /// <summary>
    /// Resolves where the editor should land after <paramref name="deletedFolderPath"/> is removed.
    /// Returns <paramref name="currentPagePath"/> unchanged when the open page is not the deleted
    /// folder and not under it. When affected, never returns null — at least the notes root.
    /// </summary>
    public static string? ResolveNextPath(
        string notesRootPath,
        string deletedFolderPath,
        string? currentPagePath)
    {
        if (string.IsNullOrWhiteSpace(notesRootPath) || string.IsNullOrWhiteSpace(deletedFolderPath))
            return currentPagePath;

        string rootFull;
        string deletedFull;
        try
        {
            rootFull = Trim(Path.GetFullPath(notesRootPath));
            deletedFull = Trim(Path.GetFullPath(deletedFolderPath));
        }
        catch
        {
            return currentPagePath;
        }

        if (string.Equals(deletedFull, rootFull, StringComparison.OrdinalIgnoreCase))
            return currentPagePath;

        if (!IsUnderOrEqual(rootFull, deletedFull))
            return currentPagePath;

        if (!IsOpenPageAffected(currentPagePath, deletedFull))
            return currentPagePath;

        var parent = Path.GetDirectoryName(deletedFull);
        if (string.IsNullOrEmpty(parent))
            return rootFull;

        var parentFull = Trim(Path.GetFullPath(parent));
        var prevSibling = FindPreviousOrNearestSibling(parentFull, deletedFull);
        if (prevSibling != null)
            return prevSibling;

        if (IsUnderOrEqual(rootFull, parentFull) && Directory.Exists(parentFull))
            return parentFull;

        return rootFull;
    }

    /// <summary>True when open path is the deleted folder or a descendant of it.</summary>
    public static bool IsOpenPageAffected(string? currentPagePath, string deletedFolderPath)
    {
        if (string.IsNullOrWhiteSpace(currentPagePath) || string.IsNullOrWhiteSpace(deletedFolderPath))
            return false;

        string currentFull;
        string deletedFull;
        try
        {
            currentFull = Trim(Path.GetFullPath(currentPagePath));
            deletedFull = Trim(Path.GetFullPath(deletedFolderPath));
        }
        catch
        {
            return false;
        }

        return IsUnderOrEqual(deletedFull, currentFull);
    }

    private static string? FindPreviousOrNearestSibling(string parentFull, string deletedFull)
    {
        if (!Directory.Exists(parentFull))
            return null;

        string[] siblings;
        try
        {
            siblings = Directory.GetDirectories(parentFull)
                .Select(d =>
                {
                    try { return Trim(Path.GetFullPath(d)); }
                    catch { return Trim(d); }
                })
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        var index = Array.FindIndex(
            siblings,
            s => string.Equals(s, deletedFull, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return null;

        if (index > 0)
            return siblings[index - 1];

        // No sibling above — nearest remaining after delete is the next sibling (if any).
        if (index + 1 < siblings.Length)
            return siblings[index + 1];

        return null;
    }

    private static string Trim(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsUnderOrEqual(string ancestor, string path) =>
        string.Equals(ancestor, path, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(ancestor + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
