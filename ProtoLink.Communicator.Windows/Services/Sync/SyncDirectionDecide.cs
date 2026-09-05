namespace ProtoLink.Communicator.Windows.Services.Sync;

/// <summary>
/// At sync start, compare local size + remote size/time against last-sync meta
/// and decide whether to upload (Write) or download (Read).
/// </summary>
public static class SyncDirectionDecide
{
    public enum Action { Write, Read, Skip, Conflict }

    public static Action Decide(
        long? localSize,
        long? remoteSize,
        long metaSize,
        DateTime? remoteTime,
        DateTime? metaTime,
        bool localContentChanged = false)
    {
        // Missing local → pull from cloud
        if (localSize == null) return Action.Read;

        // Empty local stub: always try download. Do not trust remoteSize==0 from a bad
        // Content-Length probe — that used to Skip and leave notes empty forever.
        if (localSize == 0L) return Action.Read;

        // Tiny editor stubs (e.g. "<p><br></p>" ≈ 11 bytes) stuck in meta while cloud is larger.
        if (remoteSize != null && remoteSize > localSize && localSize < 64L &&
            (localSize == metaSize || metaSize > localSize))
            return Action.Read;

        var localChanged = localSize != metaSize || localContentChanged;
        // Size vs last-sync meta. Also treat remote≠local while local still matches meta
        // (blob replaced on server without a reliable UpdateTime bump).
        var remoteSizeChanged = remoteSize != null &&
            (remoteSize != metaSize || (!localChanged && remoteSize != localSize));
        // Compare as UTC ticks so Unspecified/Local/UTC kinds from JSON don't hide newer remote.
        var remoteTimeNewer = remoteTime != null && metaTime != null &&
            ToUtcTicks(remoteTime.Value) > ToUtcTicks(metaTime.Value);
        // Listing says remote moved forward even when we never stored a prior meta time
        // (first sync after meta heal, or UpdateTime was null in store).
        var remoteTimePresentAndLocalStale = remoteTime != null && metaTime == null && !localChanged
            && remoteSize != null && remoteSize != localSize;
        var remoteChanged = remoteSizeChanged || remoteTimeNewer || remoteTimePresentAndLocalStale;

        if (localChanged && remoteChanged)
            return Action.Write; // local wins when both changed
        if (localChanged) return Action.Write;
        if (remoteChanged) return Action.Read;
        return Action.Skip;
    }

    private static long ToUtcTicks(DateTime dt) =>
        (dt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            : dt.ToUniversalTime()).Ticks;
}
