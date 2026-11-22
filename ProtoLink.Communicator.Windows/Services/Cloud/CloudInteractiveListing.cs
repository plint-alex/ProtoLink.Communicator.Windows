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
        return entity.Code == CloudEntityCodes.CloudFolder
               && string.Equals(displayName, $"folder-{entity.Id:N}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Count only CloudFolder/CloudRoot — junk CloudFiles must not win SameNamed heal.
    /// Live FAIL: twin 0bc549dd ~132 files beat folder-rich 898c1453 when raw .Count was used.
    /// </summary>
    public static int FolderChildCount(IEnumerable<GetEntitiesResult> children) =>
        children.Count(e =>
            e.Code == CloudEntityCodes.CloudFolder || e.Code == CloudEntityCodes.CloudRoot);

    /// <summary>CloudFile children only — used with <see cref="HealRichnessScore"/>.</summary>
    public static int FileChildCount(IEnumerable<GetEntitiesResult> children) =>
        children.Count(e => e.Code == CloudEntityCodes.CloudFile);

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
}
