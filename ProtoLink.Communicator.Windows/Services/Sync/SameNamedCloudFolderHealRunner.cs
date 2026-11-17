using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;

namespace ProtoLink.Communicator.Windows.Services.Sync;

/// <summary>
/// Live API discover + remap for <see cref="SameNamedCloudFolderHeal"/> (Android MainViewModel parity).
/// </summary>
public sealed class SameNamedCloudFolderHealRunner
{
    private readonly CloudApiService _api;
    private readonly JsonSyncMetadataStore _meta;

    public SameNamedCloudFolderHealRunner(CloudApiService api, JsonSyncMetadataStore? meta = null)
    {
        _api = api;
        _meta = meta ?? new JsonSyncMetadataStore();
    }

    /// <summary>
    /// Compute prefer-populated remaps. Returns null when nothing to change.
    /// Soft-fails per mapping on parent/list errors (does not abort). Caller persists + updates UI collection.
    /// </summary>
    public async Task<(int RemapCount, IReadOnlyList<CloudSyncMapping> Healed)?> TryHealAsync(
        IReadOnlyList<CloudSyncMapping> snapshot,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (snapshot.Count == 0 || userId == Guid.Empty) return null;

        var parentByMapped = new Dictionary<Guid, Guid>();
        var ancestorRootByMapped = new Dictionary<Guid, Guid>();

        foreach (var m in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var parents = await _api.GetParentEntitiesAsync(m.CloudFolderId, includeValues: false)
                    .ConfigureAwait(false);
                Guid? mainParentId = null;
                try
                {
                    var card = await _api.GetEntityCardAsync(m.CloudFolderId).ConfigureAwait(false);
                    mainParentId = card?.MainParentId;
                }
                catch
                {
                    // Soft-fail: PickParent without MainParent still works.
                }

                var parent = CloudMainParent.PickParent(mainParentId, parents);
                if (parent == null || !CloudEntityRestore.IsCloudFolderOrRoot(parent.Code))
                    continue;

                parentByMapped[m.CloudFolderId] = parent.Id;
                var root = parents.FirstOrDefault(p => p.Code == CloudEntityCodes.CloudRoot);
                if (root != null)
                    ancestorRootByMapped[m.CloudFolderId] = root.Id;
                else if (parent.Code == CloudEntityCodes.CloudRoot)
                    ancestorRootByMapped[m.CloudFolderId] = parent.Id;
            }
            catch
            {
                // Soft-fail this mapping; continue others (NFR-SYNC-1 style).
            }
        }

        if (parentByMapped.Count == 0) return null;

        var mustRoots = ancestorRootByMapped.Values.Distinct().ToList();
        var mappedNames = snapshot
            .Select(m => (m.CloudFolderName ?? "").Trim().ToLowerInvariant())
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var siblingsByParent = new Dictionary<Guid, IReadOnlyList<SameNamedCloudFolderHeal.Sibling>>();

        foreach (var parentId in parentByMapped.Values.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var siblings = await LoadChildFolderSiblingsForDiscoveryAsync(
                    parentId,
                    userId,
                    mustRoots,
                    mappedNames,
                    cancellationToken)
                .ConfigureAwait(false);
            siblingsByParent[parentId] = siblings;
        }

        var rows = snapshot
            .Select(m => new SameNamedCloudFolderHeal.MappingRow(
                m.CloudFolderId, m.LocalPath, m.CloudFolderName ?? ""))
            .ToList();
        var remaps = SameNamedCloudFolderHeal.RemapsForDuplicateNames(
            rows, siblingsByParent, parentByMapped);
        if (remaps.Count == 0) return null;

        var healedRows = SameNamedCloudFolderHeal.ApplyRemaps(rows, remaps);
        foreach (var r in remaps)
        {
            try { _meta.ClearMapping(r.FromId.ToString("N")); }
            catch { /* soft */ }
            System.Diagnostics.Debug.WriteLine(
                $"same-named heal remap {r.FromId} → {r.ToId} ({r.FolderName})");
        }

        var healed = healedRows
            .Select(r => new CloudSyncMapping
            {
                CloudFolderId = r.CloudFolderId,
                LocalPath = r.LocalPath,
                CloudFolderName = r.CloudFolderName
            })
            .ToList();
        return (remaps.Count, healed);
    }

    /// <summary>
    /// Immediate parent siblings merged with same-named folders under other CloudRoots
    /// (Drive / oldest), so dual MyNotesFolder across roots remaps.
    /// </summary>
    public async Task<IReadOnlyList<SameNamedCloudFolderHeal.Sibling>> LoadChildFolderSiblingsForDiscoveryAsync(
        Guid parentId,
        Guid userId,
        IReadOnlyList<Guid>? mustIncludeRootIds = null,
        IReadOnlySet<string>? onlyNamesLower = null,
        CancellationToken cancellationToken = default)
    {
        var local = await LoadChildFolderSiblingsRawAsync(parentId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SameNamedCloudFolderHeal.Sibling> roots;
        try
        {
            roots = await LoadUserCloudRootsRawAsync(userId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            roots = Array.Empty<SameNamedCloudFolderHeal.Sibling>();
        }

        var must = new List<Guid>();
        if (mustIncludeRootIds != null)
            must.AddRange(mustIncludeRootIds);
        if (parentId != Guid.Empty)
            must.Add(parentId);

        var rootIds = SameNamedCloudFolderHeal.CloudRootIdsForDiscovery(roots, must, maxRoots: 16);
        var fromRoots = new List<SameNamedCloudFolderHeal.Sibling>();
        foreach (var rootId in rootIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rootId == parentId) continue;
            try
            {
                fromRoots.AddRange(
                    await LoadChildFolderSiblingsRawAsync(rootId, cancellationToken).ConfigureAwait(false));
            }
            catch
            {
                // soft-fail root
            }
        }

        var merged = SameNamedCloudFolderHeal.MergeById(local, fromRoots);
        return await EnrichDuplicateSiblingChildCountsAsync(merged, onlyNamesLower, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SameNamedCloudFolderHeal.Sibling>> LoadChildFolderSiblingsRawAsync(
        Guid parentId,
        CancellationToken cancellationToken)
    {
        if (parentId == Guid.Empty || CloudVirtualNodes.IsVirtual(parentId))
            return Array.Empty<SameNamedCloudFolderHeal.Sibling>();

        cancellationToken.ThrowIfCancellationRequested();
        var children = await _api.GetEntitiesAsync(new[] { parentId }, includeValues: true)
            .ConfigureAwait(false);
        var outList = new List<SameNamedCloudFolderHeal.Sibling>();
        foreach (var e in children)
        {
            if (e.Code != CloudEntityCodes.CloudFolder && e.Code != CloudEntityCodes.CloudRoot)
                continue;
            outList.Add(ToSibling(e));
        }

        return outList;
    }

    private async Task<IReadOnlyList<SameNamedCloudFolderHeal.Sibling>> LoadUserCloudRootsRawAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty) return Array.Empty<SameNamedCloudFolderHeal.Sibling>();
        cancellationToken.ThrowIfCancellationRequested();
        var children = await _api.GetEntitiesAsync(new[] { userId }, includeValues: true)
            .ConfigureAwait(false);
        return children
            .Where(e => e.Code == CloudEntityCodes.CloudRoot)
            .Select(ToSibling)
            .ToList();
    }

    private async Task<IReadOnlyList<SameNamedCloudFolderHeal.Sibling>> EnrichDuplicateSiblingChildCountsAsync(
        IReadOnlyList<SameNamedCloudFolderHeal.Sibling> siblings,
        IReadOnlySet<string>? onlyNamesLower,
        CancellationToken cancellationToken)
    {
        if (siblings.Count < 2) return siblings;
        var dupNames = siblings
            .GroupBy(s => s.Name.Trim().ToLowerInvariant())
            .Where(g => g.Key.Length > 0 && g.Count() >= 2)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (onlyNamesLower is { Count: > 0 })
            dupNames.IntersectWith(onlyNamesLower);
        if (dupNames.Count == 0) return siblings;

        var result = new List<SameNamedCloudFolderHeal.Sibling>(siblings.Count);
        foreach (var s in siblings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!dupNames.Contains(s.Name.Trim().ToLowerInvariant()))
            {
                result.Add(s);
                continue;
            }

            var count = 0;
            try
            {
                var kids = await _api.GetEntitiesAsync(new[] { s.Id }, includeValues: false)
                    .ConfigureAwait(false);
                count = kids.Count;
            }
            catch
            {
                count = 0;
            }

            result.Add(s with { ImmediateChildCount = count });
        }

        return result;
    }

    private static SameNamedCloudFolderHeal.Sibling ToSibling(GetEntitiesResult e)
    {
        string? created = null;
        if (e.CreationTime != default && e.CreationTime > DateTime.MinValue.AddYears(1))
            created = e.CreationTime.ToUniversalTime().ToString("o");
        return new SameNamedCloudFolderHeal.Sibling(
            e.Id,
            CloudApiService.GetDisplayName(e),
            created,
            ImmediateChildCount: 0);
    }
}
