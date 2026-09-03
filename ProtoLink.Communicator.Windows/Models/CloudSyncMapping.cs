namespace ProtoLink.Communicator.Windows.Models;

public class CloudSyncMapping
{
    public Guid CloudFolderId { get; set; }
    public string LocalPath { get; set; } = string.Empty;
    public string CloudFolderName { get; set; } = string.Empty;
}
