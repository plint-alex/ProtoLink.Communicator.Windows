using System;
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
        // Mapped root deleted from a parent list (breadcrumb tip is not the mapped folder):
        // resolve LocalPath from item.Id itself — AT-CFD-6.
        var selfMapping = syncMappings.FirstOrDefault(
            x => x.CloudFolderId == item.Id && !string.IsNullOrWhiteSpace(x.LocalPath));
        if (selfMapping != null)
            return selfMapping.LocalPath;

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

    /// <summary>
    /// Removes the local mirror path when present. Missing path is a no-op.
    /// NFR-SYNC-1: IO failures soft-fail (log + return false) so Cloud delete is not aborted.
    /// </summary>
    /// <returns>True when nothing to do or delete succeeded; false on soft-failed IO.</returns>
    public static bool DeleteLocalMirror(CloudItemViewModel item, string? fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return true;
        try
        {
            if (item.IsFolder)
            {
                if (!Directory.Exists(fullPath)) return true;
                Directory.Delete(fullPath, recursive: true);
            }
            else
            {
                if (!File.Exists(fullPath)) return true;
                File.Delete(fullPath);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"DeleteLocalMirror soft-fail: {ex.GetType().Name}: {ex.Message} path={fullPath}");
            return false;
        }
    }
}
