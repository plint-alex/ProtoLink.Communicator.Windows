using System.Text.Json.Serialization;

namespace ProtoLink.Communicator.Windows.Services.Sync;

public sealed class SyncMappingInfo
{
    public string Id { get; set; } = "";
    public Guid CloudFolderId { get; set; }
    public string LocalRootPath { get; set; } = "";
    public string CloudFolderName { get; set; } = "";
}

public sealed class SyncItemMeta
{
    public string MappingId { get; set; } = "";
    public Guid RemoteId { get; set; }
    public Guid ParentRemoteId { get; set; }
    public string RelativePath { get; set; } = "";
    public bool IsFolder { get; set; }
    public long SizeBytes { get; set; }
    public DateTime? RemoteUpdateTime { get; set; }
    /// <summary>SHA-256 hex of file bytes at last sync; empty until first hash seed.</summary>
    public string ContentHash { get; set; } = "";
}

public sealed class FsEntry
{
    public string RelativePath { get; set; } = "";
    public bool IsFolder { get; set; }
    public long SizeBytes { get; set; }
    public string ContentHash { get; set; } = "";
}

public sealed class RemoteEntryInfo
{
    public Guid Id { get; set; }
    public Guid ParentId { get; set; }
    public string Name { get; set; } = "";
    public bool IsFolder { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? UpdateTime { get; set; }
}

public static class SyncPathUtil
{
    public static string Normalize(string path) =>
        path.Replace('\\', '/').Trim('/').ToLowerInvariant();

    public static string ParentOf(string relativePath)
    {
        var n = Normalize(relativePath);
        var i = n.LastIndexOf('/');
        return i < 0 ? "" : n[..i];
    }

    public static string NameOf(string relativePath)
    {
        var n = Normalize(relativePath);
        var i = n.LastIndexOf('/');
        return i < 0 ? n : n[(i + 1)..];
    }

    public static string Join(string parent, string name)
    {
        var p = Normalize(parent);
        var n = name.Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(p) ? n.ToLowerInvariant() : $"{p}/{n.ToLowerInvariant()}";
    }
}
