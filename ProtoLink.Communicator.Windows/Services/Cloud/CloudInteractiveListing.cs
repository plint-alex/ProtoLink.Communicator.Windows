using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services.Sync;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// Interactive Cloud folder listing policy: first-page paint must stay under the Cloud UI
/// HttpClient timeout even for bloated MyNotesFolder (~380 children). Values-heavy
/// <c>GetEntities</c> Take=200 can run ~15s alone / ~57s under cold-start contention and soft-fail
/// the whole list; values-light + smaller Take + deferred hydrate keeps structure Ready first.
/// </summary>
public static class CloudInteractiveListing
{
    /// <summary>
    /// Prefer StringValues name when present; else alternate (sync-meta leaf / mapping
    /// <c>CloudFolderName</c> / already-hydrated UI); else synthetic
    /// <c>folder-{id}</c> / <c>file-{id}</c> from <see cref="CloudApiService.GetDisplayName"/>.
    /// </summary>
    public static string ResolveRowName(
        GetEntitiesResult entity,
        Func<Guid, string?>? syncMetaLeafNameByRemoteId = null)
    {
        var fromValues = CloudApiService.GetDisplayName(entity);
        if (!IsSyntheticCloudDisplayName(entity, fromValues))
            return fromValues;

        var fromMeta = syncMetaLeafNameByRemoteId?.Invoke(entity.Id);
        if (!string.IsNullOrWhiteSpace(fromMeta))
            return fromMeta.Trim();

        return fromValues;
    }

    /// <summary>
    /// Values-light alternate name: sync-meta leaf, then mapping <c>CloudFolderName</c>,
    /// then a non-synthetic UI name already known (listing hydrate). Used by tree + listing
    /// so Folders tree never sticks on <c>folder-{id}</c> when map/Api Name is known.
    /// </summary>
    public static string? ResolveAlternateFolderName(
        Guid remoteId,
        string? syncMetaLeafName,
        string? mappingCloudFolderName,
        string? knownUiName = null)
    {
        if (!string.IsNullOrWhiteSpace(syncMetaLeafName))
            return syncMetaLeafName.Trim();
        if (!string.IsNullOrWhiteSpace(mappingCloudFolderName))
            return mappingCloudFolderName.Trim();
        if (!string.IsNullOrWhiteSpace(knownUiName)
            && !string.Equals(knownUiName.Trim(), $"folder-{remoteId:N}", StringComparison.OrdinalIgnoreCase))
            return knownUiName.Trim();
        return null;
    }

    public static bool IsSyntheticCloudDisplayName(GetEntitiesResult entity, string displayName)
    {
        if (CloudApiService.IsSyntheticCloudFileDisplayName(entity, displayName))
            return true;
        return CloudEntityClassify.IsFolder(entity.TypeEntityId, entity.Code)
               && !CloudEntityClassify.IsCloudRootCode(entity.Code)
               && string.Equals(displayName, $"folder-{entity.Id:N}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Count only CloudFolder/CloudRoot — junk CloudFiles must not win SameNamed heal.
    /// Live FAIL: twin 0bc549dd ~132 files beat folder-rich 898c1453 when raw .Count was used.
    /// </summary>
    public static int FolderChildCount(IEnumerable<GetEntitiesResult> children) =>
        children.Count(e => CloudEntityClassify.IsFolder(e.TypeEntityId, e.Code));

    /// <summary>CloudFile children only — used with <see cref="HealRichnessScore"/>.</summary>
    public static int FileChildCount(IEnumerable<GetEntitiesResult> children) =>
        children.Count(e => CloudEntityClassify.IsFile(e.TypeEntityId, e.Code));

    /// <summary>
    /// SameNamed heal score: folders×10 − files so near-clone junk twin
    /// (29 folders + 86 dumps) loses to owner canonical 898c1453 (28+25).
    /// </summary>
    public static int HealRichnessScore(int folderCount, int fileCount)
    {
        var folders = Math.Max(0, folderCount);
        var files = Math.Max(0, fileCount);
        return folders * 10 - files;
    }

    /// <summary>Leaf segment of a sync RelativePath without lowercasing (UI label).</summary>
    public static string LeafName(string relativePath)
    {
        var n = relativePath.Replace('\\', '/').Trim('/');
        if (n.Length == 0) return "";
        var i = n.LastIndexOf('/');
        return i < 0 ? n : n[(i + 1)..];
    }

    /// <summary>
    /// Soft Ready rows from sync-meta children of <paramref name="folderRemoteId"/> (mapped Notes).
    /// Empty when mappings/meta are absent — caller still needs GetEntities for unmapped folders.
    /// </summary>
    public static IReadOnlyList<(Guid Id, string Name, bool IsFolder, DateTime UpdateTime)> BuildSoftReadyRows(
        Guid folderRemoteId,
        IEnumerable<SyncItemMeta> children,
        int maxRows)
    {
        if (folderRemoteId == Guid.Empty || maxRows < 1)
            return Array.Empty<(Guid, string, bool, DateTime)>();

        return children
            .Where(c => c.ParentRemoteId == folderRemoteId && c.RemoteId != Guid.Empty)
            .Select(c => (
                c.RemoteId,
                string.IsNullOrWhiteSpace(c.RelativePath) ? c.RemoteId.ToString("N")[..8] : LeafName(c.RelativePath),
                c.IsFolder,
                c.RemoteUpdateTime ?? DateTime.MinValue))
            .OrderByDescending(x => x.Item3)
            .ThenByDescending(x => x.Item4)
            .ThenBy(x => x.Item2, StringComparer.OrdinalIgnoreCase)
            .Take(maxRows)
            .ToList();
    }

    /// <summary>
    /// Android <c>preferApiOrSoftReady</c>: Api kids always win; Soft Ready paints when Api is empty
    /// (timeout race / sick GetEntities returning []). Catch-path Soft Ready alone is not enough.
    /// </summary>
    public static IReadOnlyList<T> PreferApiOrSoftReady<T>(
        IReadOnlyList<T>? apiRows,
        IReadOnlyList<T>? softReadyRows)
    {
        if (apiRows is { Count: > 0 })
            return apiRows;
        return softReadyRows is { Count: > 0 }
            ? softReadyRows
            : Array.Empty<T>();
    }

    /// <summary>
    /// IncludeValues hydrate must not blank Soft Ready / values-light Ready when the values
    /// page returns [] (Android <c>mergeHydrateById</c> empty→keep light).
    /// </summary>
    public static bool ShouldKeepListingOverEmptyHydrate(int paintedCount, int hydrateCount) =>
        paintedCount > 0 && hydrateCount == 0;

    /// <summary>
    /// CloudRoot for mapping Soft Ready: known set, My Cloud id, or legacy Code=CloudRoot.
    /// Known-only missed LastOpen Soft Ready when kind-parent discover timed out under AutoPush.
    /// </summary>
    public static bool IsCloudRootForMappingSoftReady(
        Guid folderRemoteId,
        IReadOnlySet<Guid>? knownCloudRootIds,
        Guid myCloudRootId = default,
        string? folderCode = null)
    {
        if (folderRemoteId == Guid.Empty) return false;
        if (myCloudRootId != Guid.Empty && myCloudRootId == folderRemoteId)
            return true;
        if (CloudEntityClassify.IsCloudRootCode(folderCode))
            return true;
        return knownCloudRootIds != null && knownCloudRootIds.Contains(folderRemoteId);
    }

    /// <summary>
    /// Soft Ready when sync-meta is empty/quarantined under a CloudRoot: paint mapped folders
    /// so CloudRoot Soft Ready is not sticky-empty while Api kids exist (WIN_CLOUD_SOFTREADY_EMPTY_LISTING).
    /// </summary>
    public static IReadOnlyList<(Guid Id, string Name, bool IsFolder, DateTime UpdateTime)> BuildSoftReadyRowsFromMappings(
        Guid folderRemoteId,
        IEnumerable<(Guid CloudFolderId, string CloudFolderName)> mappings,
        IReadOnlySet<Guid>? knownCloudRootIds,
        int maxRows,
        Guid myCloudRootId = default,
        string? folderCode = null)
    {
        if (folderRemoteId == Guid.Empty || maxRows < 1 || mappings == null)
            return Array.Empty<(Guid, string, bool, DateTime)>();

        if (!IsCloudRootForMappingSoftReady(folderRemoteId, knownCloudRootIds, myCloudRootId, folderCode))
            return Array.Empty<(Guid, string, bool, DateTime)>();

        return mappings
            .Where(m => m.CloudFolderId != Guid.Empty && m.CloudFolderId != folderRemoteId)
            .Select(m => (
                m.CloudFolderId,
                string.IsNullOrWhiteSpace(m.CloudFolderName)
                    ? $"folder-{m.CloudFolderId:N}"
                    : m.CloudFolderName.Trim(),
                true,
                DateTime.MinValue))
            .OrderBy(x => x.Item2, StringComparer.OrdinalIgnoreCase)
            .Take(maxRows)
            .ToList();
    }
}
