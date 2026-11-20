using ProtoLink.Communicator.Windows.Core;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// E10 Cloud folder delete — recursive DeleteEntity order (Api has no cascade).
/// Mirrors Android <c>CloudFolderDelete.deleteFolderRecursive</c>.
/// </summary>
public static class CloudFolderDelete
{
    public readonly record struct ChildRef(Guid Id, string? Code);

    /// <summary>
    /// Depth-first: delete CloudFile children, recurse CloudFolder children, then delete
    /// <paramref name="folderId"/>. Skips self-id rows and non CloudFile/CloudFolder codes.
    /// </summary>
    public static async Task DeleteFolderRecursiveAsync(
        Guid folderId,
        Func<Guid, Task<IReadOnlyList<ChildRef>>> listChildren,
        Func<Guid, Task> deleteEntity,
        ICollection<Guid>? deletedIds = null)
    {
        var children = await listChildren(folderId).ConfigureAwait(false);
        foreach (var c in children)
        {
            if (c.Id == folderId)
                continue;

            if (string.Equals(c.Code, CloudEntityCodes.CloudFile, StringComparison.Ordinal))
            {
                await deleteEntity(c.Id).ConfigureAwait(false);
                deletedIds?.Add(c.Id);
            }
            else if (string.Equals(c.Code, CloudEntityCodes.CloudFolder, StringComparison.Ordinal))
            {
                await DeleteFolderRecursiveAsync(c.Id, listChildren, deleteEntity, deletedIds)
                    .ConfigureAwait(false);
            }
        }

        await deleteEntity(folderId).ConfigureAwait(false);
        deletedIds?.Add(folderId);
    }
}
