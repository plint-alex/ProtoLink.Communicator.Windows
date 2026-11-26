using ProtoLink.Communicator.Windows.Core;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// E10 Cloud folder delete — production path is <strong>single-root</strong>
/// <c>DeleteEntity</c> (Api cascade soft-hides the Cloud subtree). Local Soft Ready /
/// listing meta / tombstones are purged from <em>known UI/cache</em> ids — never via
/// extra DeleteEntity RTTs. Recursive helper remains for offline unit tests only.
/// Mirrors Android <c>CloudFolderDelete</c>.
/// </summary>
public static class CloudFolderDelete
{
    public readonly record struct ChildRef(Guid Id, string? Code, Guid? TypeEntityId = null);

    /// <summary>
    /// Production delete: one DeleteEntity on <paramref name="rootId"/>; Api cascade
    /// owns the Cloud subtree. Adds <paramref name="rootId"/> to <paramref name="deletedIds"/>.
    /// Fail-closed: if delete throws, caller must not pretend gone / soft-hide locally alone.
    /// </summary>
    public static async Task DeleteSingleRootAsync(
        Guid rootId,
        Func<Guid, Task> deleteEntity,
        ICollection<Guid>? deletedIds = null)
    {
        await deleteEntity(rootId).ConfigureAwait(false);
        deletedIds?.Add(rootId);
    }

    /// <summary>
    /// BFS over <em>local</em> known children (folder-tree cache / sync-meta / listing) —
    /// no HTTP. Collects folder + file ids under <paramref name="rootId"/> into
    /// <paramref name="into"/> (includes root). Used after single-root DeleteEntity to
    /// purge Soft Ready / disk cache / tombstones without re-walking the Api.
    /// </summary>
    public static void CollectKnownSubtreeIds(
        Guid rootId,
        Func<Guid, IEnumerable<ChildRef>> knownChildren,
        ICollection<Guid> into)
    {
        ArgumentNullException.ThrowIfNull(knownChildren);
        ArgumentNullException.ThrowIfNull(into);

        var seen = new HashSet<Guid>();
        var queue = new Queue<Guid>();

        void Remember(Guid id)
        {
            if (!seen.Add(id)) return;
            into.Add(id);
        }

        Remember(rootId);
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            IEnumerable<ChildRef> kids;
            try { kids = knownChildren(parent) ?? Array.Empty<ChildRef>(); }
            catch { continue; }

            foreach (var c in kids)
            {
                if (c.Id == Guid.Empty || c.Id == parent)
                    continue;
                if (!seen.Add(c.Id))
                    continue;
                into.Add(c.Id);
                if (CloudEntityClassify.IsFolder(c.TypeEntityId, c.Code))
                    queue.Enqueue(c.Id);
            }
        }
    }

    /// <summary>
    /// Offline unit-test helper only — leaf-first recursive DeleteEntity.
    /// <strong>Do not</strong> call from UI delete or Sync ApplyLocal (Api cascade +
    /// <see cref="DeleteSingleRootAsync"/> is the production path; N+1 recurse is too slow).
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
