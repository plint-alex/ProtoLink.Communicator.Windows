using ProtoLink.Communicator.Windows.Core;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// E10 Cloud folder delete — leaf-first recursive DeleteEntity (defense-in-depth while/after
/// Api CloudFolder cascade). Children must never promote to the deleted folder’s level.
/// Mirrors Android <c>CloudFolderDelete.deleteFolderRecursive</c>.
/// </summary>
public static class CloudFolderDelete
{
    public readonly record struct ChildRef(Guid Id, string? Code, Guid? TypeEntityId = null);

    /// <summary>
    /// Depth-first: delete file children, recurse folder children, then delete
    /// <paramref name="folderId"/>. Skips self-id rows and non cloud folder/file rows.
    /// Fail-closed: if <paramref name="listChildren"/> throws or returns null, aborts
    /// without deleting <paramref name="folderId"/> (do not soft-hide parent alone → promote).
    /// </summary>
    public static async Task DeleteFolderRecursiveAsync(
        Guid folderId,
        Func<Guid, Task<IReadOnlyList<ChildRef>>> listChildren,
        Func<Guid, Task> deleteEntity,
        ICollection<Guid>? deletedIds = null)
    {
        var children = await listChildren(folderId).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Cloud folder delete listing returned null under {folderId:D}; aborting to avoid promote.");

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
