using System.IO;
using System.Linq;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Services;

public class NotesFileSystemService
{
    public FileSystemItem? BuildTree(string rootPath)
    {
        if (!Directory.Exists(rootPath)) return null;
        var root = FileSystemItem.CreateFolder(Path.GetFullPath(rootPath));
        BuildRecursive(root);
        return root;
    }

    private void BuildRecursive(FileSystemItem folder)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.GetDirectories(folder.FullPath).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            string full;
            try { full = Path.GetFullPath(dir); }
            catch { full = dir; }
            if (!seen.Add(full)) continue;
            var child = FileSystemItem.CreateFolder(full, folder);
            BuildRecursive(child);
            folder.Children.Add(child);
        }
    }

    public bool HasIndexFile(string folderPath)
    {
        return File.Exists(Path.Combine(folderPath, "index.html")) ||
               File.Exists(Path.Combine(folderPath, "index.htm"));
    }

    public string GetIndexFilePath(string folderPath)
    {
        var html = Path.Combine(folderPath, "index.html");
        return File.Exists(html) ? html : Path.Combine(folderPath, "index.htm");
    }

    /// <summary>
    /// Path to write: prefer existing <c>index.htm</c>, otherwise <c>index.html</c>.
    /// </summary>
    public string GetWriteIndexFilePath(string folderPath)
    {
        var htm = Path.Combine(folderPath, "index.htm");
        if (File.Exists(htm))
            return htm;
        return Path.Combine(folderPath, "index.html");
    }

    public void CreateFolder(string parentPath, string folderName)
    {
        Directory.CreateDirectory(Path.Combine(parentPath, folderName));
    }

    public void DeleteItem(string path, bool isFolder)
    {
        if (isFolder) Directory.Delete(path, true);
    }

    public void RenameItem(string oldPath, string newName, bool isFolder)
    {
        if (!isFolder) return;
        var dir = Path.GetDirectoryName(oldPath);
        if (dir == null) return;
        Directory.Move(oldPath, Path.Combine(dir, newName));
    }

    /// <summary>
    /// Move a note folder under a new parent (same volume). Name is preserved.
    /// Caller must validate guards (root / self / descendant / collision).
    /// </summary>
    public string MoveFolder(string sourcePath, string targetParentPath)
    {
        var sourceFull = Path.GetFullPath(sourcePath);
        var targetFull = Path.GetFullPath(targetParentPath);
        var name = Path.GetFileName(sourceFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name))
            throw new InvalidOperationException("Cannot determine folder name to move.");
        var dest = Path.Combine(targetFull, name);
        Directory.Move(sourceFull, dest);
        return Path.GetFullPath(dest);
    }

    /// <summary>
    /// Validate a Notes-tree reparent. Returns null when allowed; otherwise a StatusText message.
    /// </summary>
    public static string? ValidateMove(string sourcePath, string targetParentPath, string notesRootPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            string.IsNullOrWhiteSpace(targetParentPath) ||
            string.IsNullOrWhiteSpace(notesRootPath))
            return "Invalid path.";

        string sourceFull;
        string targetFull;
        string rootFull;
        try
        {
            sourceFull = Path.GetFullPath(sourcePath);
            targetFull = Path.GetFullPath(targetParentPath);
            rootFull = Path.GetFullPath(notesRootPath);
        }
        catch
        {
            return "Invalid path.";
        }

        rootFull = TrimPath(rootFull);
        sourceFull = TrimPath(sourceFull);
        targetFull = TrimPath(targetFull);

        if (string.Equals(sourceFull, rootFull, StringComparison.OrdinalIgnoreCase))
            return "Cannot move the notes root folder.";

        if (!IsUnderOrEqual(rootFull, sourceFull))
            return "Move must stay inside the Notes folder.";

        if (!IsUnderOrEqual(rootFull, targetFull))
            return "Move must stay inside the Notes folder.";

        if (!Directory.Exists(sourceFull))
            return "Page not found.";

        if (!Directory.Exists(targetFull))
            return "Target folder not found.";

        if (string.Equals(sourceFull, targetFull, StringComparison.OrdinalIgnoreCase))
            return "Cannot drop a page onto itself.";

        if (IsStrictDescendant(sourceFull, targetFull))
            return "Cannot move a page into its own subpage.";

        var currentParent = Path.GetDirectoryName(sourceFull);
        if (!string.IsNullOrEmpty(currentParent) &&
            string.Equals(TrimPath(Path.GetFullPath(currentParent)), targetFull, StringComparison.OrdinalIgnoreCase))
            return "Already in that folder.";

        var name = Path.GetFileName(sourceFull);
        var dest = Path.Combine(targetFull, name);
        if (Directory.Exists(dest))
            return $"A page named '{name}' already exists there.";

        return null;
    }

    private static string TrimPath(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsUnderOrEqual(string ancestor, string path) =>
        string.Equals(ancestor, path, StringComparison.OrdinalIgnoreCase) ||
        IsStrictDescendant(ancestor, path);

    private static bool IsStrictDescendant(string ancestor, string path)
    {
        var prefix = ancestor + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
