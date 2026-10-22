using System.IO;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// Pure enumerator for Notes E5/E5a: direct-child files under the open note folder,
/// excluding Notes-owned index.html / index.htm (case-insensitive). Directories are
/// omitted — the left Notes tree already owns folder navigation.
/// </summary>
public static class NotesSiblingFiles
{
    public static bool IsNotesOwnedFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        return fileName.Equals("index.html", StringComparison.OrdinalIgnoreCase)
               || fileName.Equals("index.htm", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Filtered list of direct-child files. Returns null when the folder is missing,
    /// inaccessible, or has no qualifying files (panel should hide). Child directories
    /// alone never keep the panel open (E5a).
    /// </summary>
    public static FileSystemItem? BuildTree(string? noteFolderPath)
    {
        if (string.IsNullOrWhiteSpace(noteFolderPath)) return null;

        string full;
        try
        {
            full = Path.GetFullPath(noteFolderPath);
        }
        catch
        {
            return null;
        }

        if (!Directory.Exists(full)) return null;

        try
        {
            var root = FileSystemItem.CreateFolder(full);
            AddDirectChildFiles(root);
            return root.Children.Count == 0 ? null : root;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void AddDirectChildFiles(FileSystemItem folder)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(folder.FullPath)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (IsNotesOwnedFileName(name)) continue;
            string full;
            try { full = Path.GetFullPath(file); }
            catch { full = file; }
            folder.Children.Add(FileSystemItem.CreateFile(full, folder));
        }
    }
}
