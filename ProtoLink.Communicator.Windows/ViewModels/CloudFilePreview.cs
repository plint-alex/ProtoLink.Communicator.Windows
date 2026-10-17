using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.ViewModels;

public enum CloudFilePreviewKind
{
    Image,
    Text,
    Pdf,
    Other
}

/// <summary>Pure pick-source / type → preview kind helpers (offline-testable).</summary>
public static class CloudFilePreview
{
    public static CloudFilePreviewKind KindFromFileName(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" => CloudFilePreviewKind.Image,
            ".txt" or ".md" or ".json" or ".csv" or ".log" => CloudFilePreviewKind.Text,
            ".pdf" => CloudFilePreviewKind.Pdf,
            _ => CloudFilePreviewKind.Other
        };
    }

    /// <summary>
    /// Local mirror path when the file sits under a mapped folder and the file exists on disk; otherwise null (fetch remote).
    /// </summary>
    public static string? TryGetExistingMirrorPath(
        string fileName,
        IReadOnlyList<CloudBreadcrumbEntry> breadcrumbPath,
        IEnumerable<CloudSyncMapping> syncMappings)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var item = new CloudItemViewModel { Name = fileName, IsFolder = false };
        var path = CloudMappingMirrorPaths.GetLocalPathForCloudItem(item, breadcrumbPath, syncMappings);
        return path != null && File.Exists(path) ? path : null;
    }

    /// <summary>True when preview/Open should read the local mirror instead of <c>api/Files/getFile</c>.</summary>
    public static bool PreferLocalMirror(string? existingMirrorPath) =>
        !string.IsNullOrEmpty(existingMirrorPath);
}
