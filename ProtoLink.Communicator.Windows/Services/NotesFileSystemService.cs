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
}
