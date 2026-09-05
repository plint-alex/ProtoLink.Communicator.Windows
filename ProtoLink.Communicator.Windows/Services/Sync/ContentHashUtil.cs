using System.IO;
using System.Security.Cryptography;

namespace ProtoLink.Communicator.Windows.Services.Sync;

public static class ContentHashUtil
{
    public static string Sha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Sha256HexFile(string fullPath)
    {
        using var stream = File.OpenRead(fullPath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// True when meta already has a hash and local bytes differ (same-size content edits).
    /// </summary>
    public static bool IsLocalContentChanged(string metaHash, string localHash) =>
        !string.IsNullOrEmpty(metaHash)
        && !string.IsNullOrEmpty(localHash)
        && !string.Equals(metaHash, localHash, StringComparison.OrdinalIgnoreCase);

    public static bool IsUtcNewer(DateTime remote, DateTime meta)
    {
        static long Ticks(DateTime dt) =>
            (dt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                : dt.ToUniversalTime()).Ticks;
        return Ticks(remote) > Ticks(meta);
    }

    /// <summary>
    /// Convergence decision once both local and remote content hashes are known.
    /// Never call with an empty remoteHash — download first; on download failure throw.
    /// Ambiguous cases (empty meta, both diverged) → Conflict (no silent LWW / Write bias).
    /// </summary>
    public static SyncDirectionDecide.Action DecideByHash(
        string localHash,
        string remoteHash,
        string? metaHash,
        DateTime? remoteUpdateTime = null,
        DateTime? metaUpdateTime = null)
    {
        if (string.IsNullOrEmpty(localHash))
            return SyncDirectionDecide.Action.Read;
        if (string.IsNullOrEmpty(remoteHash))
            throw new ArgumentException("remoteHash required; download remote bytes before DecideByHash", nameof(remoteHash));

        if (string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase))
            return SyncDirectionDecide.Action.Skip;

        var metaEmpty = string.IsNullOrEmpty(metaHash);
        var localMatchesMeta = !metaEmpty
            && string.Equals(localHash, metaHash, StringComparison.OrdinalIgnoreCase);
        var remoteMatchesMeta = !metaEmpty
            && string.Equals(remoteHash, metaHash, StringComparison.OrdinalIgnoreCase);

        if (localMatchesMeta && !remoteMatchesMeta)
            return SyncDirectionDecide.Action.Read;

        if (remoteMatchesMeta && !localMatchesMeta)
            return SyncDirectionDecide.Action.Write;

        // Empty baseline or both sides diverged — do not guess.
        _ = remoteUpdateTime;
        _ = metaUpdateTime;
        return SyncDirectionDecide.Action.Conflict;
    }
}
