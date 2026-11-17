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
        // Shared read: hashing runs outside the write gate and must not collide with a note save.
        using var stream = MappedFolderIoGate.OpenSharedRead(fullPath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Hash local bytes, or null when the path is already gone (NFR-SYNC-1).
    /// Does not swallow other IO errors (sharing violations, access denied, etc.).
    /// </summary>
    public static string? TrySha256HexFile(string fullPath)
    {
        try
        {
            return Sha256HexFile(fullPath);
        }
        catch (Exception ex) when (SyncMissingLocalPath.IsMissing(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// True when meta already has a hash and local bytes differ (same-size content edits).
    /// </summary>
    public static bool IsLocalContentChanged(string metaHash, string localHash) =>
        !string.IsNullOrEmpty(metaHash)
        && !string.IsNullOrEmpty(localHash)
        && !string.Equals(metaHash, localHash, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Skip getFile probe when local bytes still match the last-sync baseline and the remote
    /// listing is not newer. Avoids re-downloading every file on large mapped trees (MyNotesFolder
    /// ~10k) so deferred full Sync after autopush preempt can stamp LastSync in-bounds.
    /// </summary>
    public static bool CanSkipRemoteContentDownload(
        string localHash,
        string? metaHash,
        DateTime? remoteUpdateTime,
        DateTime? metaUpdateTime)
    {
        if (string.IsNullOrEmpty(localHash) || string.IsNullOrEmpty(metaHash))
            return false;
        if (!string.Equals(localHash, metaHash, StringComparison.OrdinalIgnoreCase))
            return false;
        return !IsRemoteListingNewer(remoteUpdateTime, metaUpdateTime);
    }

    /// <summary>
    /// ApplyCompare fast path: size still matches meta, baseline hash present, remote listing not
    /// newer → skip SHA-256 of local bytes (and getFile). Same-size silent edits are caught by
    /// interval Push (hashing SnapshotFs) or Force Upload; full reconcile must stay inside the
    /// daily-use LastSync window on ~10k MyNotesFolder trees.
    /// </summary>
    public static bool CanSkipCompareWithoutHash(
        long localSize,
        long metaSize,
        string? metaHash,
        DateTime? remoteUpdateTime,
        DateTime? metaUpdateTime)
    {
        if (localSize != metaSize) return false;
        if (string.IsNullOrEmpty(metaHash)) return false;
        return !IsRemoteListingNewer(remoteUpdateTime, metaUpdateTime);
    }

    /// <summary>
    /// True when the remote listing UpdateTime is strictly newer than the meta baseline.
    /// Null remote UpdateTime → not newer (trust baseline; Force Download repairs).
    /// </summary>
    public static bool IsRemoteListingNewer(DateTime? remoteUpdateTime, DateTime? metaUpdateTime)
    {
        if (remoteUpdateTime is not DateTime remote) return false;
        if (metaUpdateTime is not DateTime meta) return true;
        return IsUtcNewer(remote, meta);
    }

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
    /// Never call with an empty remoteHash string — download first; on null download soft-fail
    /// the item (caller). Hash of empty remote bytes is a valid remoteHash.
    /// Ambiguous cases (empty meta, both diverged) → Read (cloud wins) so every device
    /// mapped to the same CloudFolderId ends with the same bytes (delete/overwrite OK).
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

        // Empty baseline or both sides diverged — cloud is the shared truth across devices.
        _ = remoteUpdateTime;
        _ = metaUpdateTime;
        return SyncDirectionDecide.Action.Read;
    }
}
