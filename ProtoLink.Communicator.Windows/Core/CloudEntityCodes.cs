namespace ProtoLink.Communicator.Windows.Core;

public static class CloudEntityCodes
{
    public const string CloudRoot = "CloudRoot";
    public const string CloudFolder = "CloudFolder";
    public const string CloudFile = "CloudFile";
    public static readonly Guid NameTypeId = new("00010003-0000-0000-0000-000000000000");
    public static readonly Guid MimeTypeId = new("00010012-0000-0000-0000-000000000000");

    /// <summary>Api seed typeCloudFolder (CloudTreeSeedIds.CloudFolderTypeEntityId).</summary>
    public static readonly Guid TypeCloudFolderId = new("b0000005-0000-4000-8000-000000000001");

    /// <summary>Api seed typeCloudFile (CloudTreeSeedIds.CloudFileTypeEntityId).</summary>
    public static readonly Guid TypeCloudFileId = new("b0000005-0000-4000-8000-000000000002");

    /// <summary>Api seed kind parent root (CloudTreeSeedIds.RootKindEntityId).</summary>
    public static readonly Guid RootKindEntityId = new("b0000005-0000-4000-8000-000000000011");

    /// <summary>Api seed kind parent folder (CloudTreeSeedIds.FolderKindEntityId).</summary>
    public static readonly Guid FolderKindEntityId = new("b0000005-0000-4000-8000-000000000012");
}
