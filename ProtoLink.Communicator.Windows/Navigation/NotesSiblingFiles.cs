using System.IO;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// Pure enumerator for Notes E5: local children under the open note folder,
/// excluding Notes-owned index.html / index.htm (case-insensitive).
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
    /// Filtered recursive tree of siblings. Returns null when the folder is missing,
    /// inaccessible, or has no non-owned children (panel should hide).
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
            BuildChildren(root);
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

    private static void BuildChildren(FileSystemItem folder)
    {
        IEnumerable<string> dirs;
        IEnumerable<string> files;
        try
        {
            dirs = Directory.GetDirectories(folder.FullPath)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
            files = Directory.GetFiles(folder.FullPath)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        foreach (var dir in dirs)
        {
            string full;
            try { full = Path.GetFullPath(dir); }
            catch { full = dir; }
            var child = FileSystemItem.CreateFolder(full, folder);
            BuildChildren(child);
            folder.Children.Add(child);
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
