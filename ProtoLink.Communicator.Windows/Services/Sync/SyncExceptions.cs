namespace ProtoLink.Communicator.Windows.Services.Sync;

public class SyncException : Exception
{
    public string? RelativePath { get; }
    public string? LocalHash { get; }
    public string? RemoteHash { get; }
    public string? MetaHash { get; }

    public SyncException(
        string message,
        string? relativePath = null,
        string? localHash = null,
        string? remoteHash = null,
        string? metaHash = null,
        Exception? inner = null)
        : base(Format(message, relativePath, localHash, remoteHash, metaHash), inner)
    {
        RelativePath = relativePath;
        LocalHash = localHash;
        RemoteHash = remoteHash;
        MetaHash = metaHash;
    }

    private static string Format(
        string message,
        string? relativePath,
        string? localHash,
        string? remoteHash,
        string? metaHash)
    {
        var parts = new List<string> { message };
        if (!string.IsNullOrEmpty(relativePath)) parts.Add($"path={relativePath}");
        if (!string.IsNullOrEmpty(localHash)) parts.Add($"local={localHash[..Math.Min(12, localHash.Length)]}…");
        if (!string.IsNullOrEmpty(remoteHash)) parts.Add($"remote={remoteHash[..Math.Min(12, remoteHash.Length)]}…");
        if (!string.IsNullOrEmpty(metaHash)) parts.Add($"meta={metaHash[..Math.Min(12, metaHash.Length)]}…");
        return string.Join(" | ", parts);
    }
}

public sealed class SyncConflictException : SyncException
{
    public SyncConflictException(
        string relativePath,
        string localHash,
        string remoteHash,
        string? metaHash,
        string reason)
        : base(reason, relativePath, localHash, remoteHash, metaHash)
    {
    }
}
