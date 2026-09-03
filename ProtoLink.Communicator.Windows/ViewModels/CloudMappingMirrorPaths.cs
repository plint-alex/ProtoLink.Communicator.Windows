using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.ViewModels;

public static class CloudMappingMirrorPaths
{
    /// <summary>True if <paramref name="cloudFolderId"/> appears in the breadcrumb and lies under a mapped root; outputs that root and the local directory for the cloud folder.</summary>
    public static bool TryGetSyncedMirror(
        Guid cloudFolderId,
        IReadOnlyList<CloudBreadcrumbEntry> breadcrumbPath,
        IEnumerable<CloudSyncMapping> syncMappings,
        out CloudSyncMapping rootMapping,
        out string localDirectory)
    {
        rootMapping = null!;
        localDirectory = null!;
        var idx = -1;
        for (var i = 0; i < breadcrumbPath.Count; i++)
        {
            if (breadcrumbPath[i].Id == cloudFolderId)
            {
                idx = i;
                break;
            }
        }

        if (idx < 0) return false;

        CloudSyncMapping? mapping = null;
        var mapIndex = -1;
        for (var i = idx; i >= 0; i--)
        {
            var m = syncMappings.FirstOrDefault(x => x.CloudFolderId == breadcrumbPath[i].Id);
            if (m != null && !string.IsNullOrWhiteSpace(m.LocalPath))
            {
                mapping = m;
                mapIndex = i;
                break;
            }
        }

        if (mapping == null) return false;

        rootMapping = mapping;
        var segments = new List<string> { mapping.LocalPath };
        for (var j = mapIndex + 1; j <= idx; j++)
            segments.Add(breadcrumbPath[j].Name);
        localDirectory = Path.Combine(segments.ToArray());
        return true;
    }

    /// <summary>Parent cloud folder must be the current breadcrumb tip. Child is a direct subfolder (e.g. row in the list).</summary>
    public static bool TryGetSyncedMirrorForChildFolder(
        Guid parentCloudFolderId,
        string childFolderName,
        IReadOnlyList<CloudBreadcrumbEntry> breadcrumbPath,
        IEnumerable<CloudSyncMapping> syncMappings,
        out CloudSyncMapping rootMapping,
        out string localDirectoryForChild)
    {
        rootMapping = null!;
        localDirectoryForChild = null!;
        if (breadcrumbPath.Count == 0 || breadcrumbPath[^1].Id != parentCloudFolderId) return false;
        if (!TryGetSyncedMirror(parentCloudFolderId, breadcrumbPath, syncMappings, out rootMapping, out var parentLocal))
            return false;
        localDirectoryForChild = Path.Combine(parentLocal, childFolderName);
        return true;
    }

    public static string? GetLocalPathForCloudItem(
        CloudItemViewModel item,
        IReadOnlyList<CloudBreadcrumbEntry> breadcrumbPath,
        IEnumerable<CloudSyncMapping> syncMappings)
    {
        CloudSyncMapping? mapping = null;
        var mapIndex = -1;
        for (var i = breadcrumbPath.Count - 1; i >= 0; i--)
        {
            var m = syncMappings.FirstOrDefault(x => x.CloudFolderId == breadcrumbPath[i].Id);
            if (m != null && !string.IsNullOrWhiteSpace(m.LocalPath))
            {
                mapping = m;
                mapIndex = i;
                break;
            }
        }

        if (mapping == null) return null;
        var segments = new List<string> { mapping.LocalPath };
        for (var j = mapIndex + 1; j < breadcrumbPath.Count; j++)
            segments.Add(breadcrumbPath[j].Name);
        segments.Add(item.Name);
        return Path.Combine(segments.ToArray());
    }

    public static void DeleteLocalMirror(CloudItemViewModel item, string? fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return;
        if (item.IsFolder && Directory.Exists(fullPath))
            Directory.Delete(fullPath, recursive: true);
        else if (!item.IsFolder && File.Exists(fullPath))
            File.Delete(fullPath);
    }
}
