using ProtoLink.Communicator.Windows.Core;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// E10 Cloud folder delete — recursive DeleteEntity order (Api has no cascade).
/// Mirrors Android <c>CloudFolderDelete.deleteFolderRecursive</c>.
/// </summary>
public static class CloudFolderDelete
{
    public readonly record struct ChildRef(Guid Id, string? Code, Guid? TypeEntityId = null);

    /// <summary>
    /// Depth-first: delete file children, recurse folder children, then delete
    /// <paramref name="folderId"/>. Skips self-id rows and non cloud folder/file rows.
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

            if (CloudEntityClassify.IsFile(c.TypeEntityId, c.Code))
            {
                await deleteEntity(c.Id).ConfigureAwait(false);
                deletedIds?.Add(c.Id);
            }
            else if (CloudEntityClassify.IsFolder(c.TypeEntityId, c.Code))
            {
                await DeleteFolderRecursiveAsync(c.Id, listChildren, deleteEntity, deletedIds)
                    .ConfigureAwait(false);
            }
        }

        await deleteEntity(folderId).ConfigureAwait(false);
        deletedIds?.Add(folderId);
    }
}
