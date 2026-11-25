namespace ProtoLink.Communicator.Windows.Core;

/// <summary>
/// Folder vs file from TypeEntityId (Code=display name). Legacy Code discriminators remain fallback.
/// CloudRoot has no TypeEntityId seed — detect via legacy Code or RootKind parent.
/// </summary>
public static class CloudEntityClassify
{
    public static bool IsFolder(Guid? typeEntityId, string? code)
    {
        if (typeEntityId == CloudEntityCodes.TypeCloudFolderId) return true;
        if (typeEntityId == CloudEntityCodes.TypeCloudFileId) return false;
        return code is CloudEntityCodes.CloudFolder or CloudEntityCodes.CloudRoot;
    }

    public static bool IsFile(Guid? typeEntityId, string? code)
    {
        if (typeEntityId == CloudEntityCodes.TypeCloudFileId) return true;
        if (typeEntityId == CloudEntityCodes.TypeCloudFolderId) return false;
        return string.Equals(code, CloudEntityCodes.CloudFile, StringComparison.Ordinal);
    }

    /// <summary>Cloud folder or file row (sync/delete walk). Skips unrelated entities.</summary>
    public static bool IsCloudFolderOrFile(Guid? typeEntityId, string? code) =>
        IsFolder(typeEntityId, code) || IsFile(typeEntityId, code);

    public static bool IsCloudRootCode(string? code) =>
        string.Equals(code, CloudEntityCodes.CloudRoot, StringComparison.Ordinal);

    /// <summary>
    /// CloudRoot after Code=name: legacy discriminator Code, and/or RootKind parent
    /// (Api <c>CloudTreeSeedIds.RootKindEntityId</c>).
    /// </summary>
    public static bool IsCloudRoot(string? code, IEnumerable<Guid>? parentIds = null) =>
        IsCloudRootCode(code)
        || (parentIds != null && parentIds.Contains(CloudEntityCodes.RootKindEntityId));

    /// <summary>
    /// Navigable Cloud tree row: folder/file by type/Code, legacy CloudRoot Code, or known CloudRoot id.
    /// </summary>
    public static bool IsNavigableCloudEntity(
        Guid? typeEntityId,
        string? code,
        bool isKnownCloudRoot = false) =>
        isKnownCloudRoot
        || IsCloudRootCode(code)
        || IsCloudFolderOrFile(typeEntityId, code);
}
