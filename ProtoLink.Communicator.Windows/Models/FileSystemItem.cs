using System.Collections.ObjectModel;
using System.IO;

namespace ProtoLink.Communicator.Windows.Models;

public enum FileSystemItemType
{
    Folder,
    File
}

public class FileSystemItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public FileSystemItemType Type { get; set; }
    public ObservableCollection<FileSystemItem> Children { get; set; } = new();
    public FileSystemItem? Parent { get; set; }

    public static FileSystemItem CreateFolder(string path, FileSystemItem? parent = null)
    {
        return new FileSystemItem
        {
            Name = Path.GetFileName(path),
            FullPath = path,
            Type = FileSystemItemType.Folder,
            Parent = parent
        };
    }
}
