using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// Interactive Cloud folder listing policy: first-page paint must stay under the Cloud UI
/// HttpClient timeout even for bloated MyNotesFolder (~380 children). Values-heavy
/// <c>GetEntities</c> Take=200 can run ~15s alone / ~57s under cold-start contention and soft-fail
/// the whole list; values-light is API-cached and paints structure first.
/// </summary>
public static class CloudInteractiveListing
{
    /// <summary>
    /// Prefer StringValues name when present; else sync-meta RelativePath leaf; else synthetic
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

    public static bool IsSyntheticCloudDisplayName(GetEntitiesResult entity, string displayName)
    {
        if (CloudApiService.IsSyntheticCloudFileDisplayName(entity, displayName))
            return true;
        return entity.Code == CloudEntityCodes.CloudFolder
               && string.Equals(displayName, $"folder-{entity.Id:N}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Leaf segment of a sync RelativePath without lowercasing (UI label).</summary>
    public static string LeafName(string relativePath)
    {
        var n = relativePath.Replace('\\', '/').Trim('/');
        if (n.Length == 0) return "";
        var i = n.LastIndexOf('/');
        return i < 0 ? n : n[(i + 1)..];
    }
}
