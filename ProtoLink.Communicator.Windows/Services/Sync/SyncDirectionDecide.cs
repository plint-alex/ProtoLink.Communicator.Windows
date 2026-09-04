namespace ProtoLink.Communicator.Windows.Services.Sync;

/// <summary>
/// At sync start, compare local size + remote size/time against last-sync meta
/// and decide whether to upload (Write) or download (Read).
/// </summary>
public static class SyncDirectionDecide
{
    public enum Action { Write, Read, Skip }

    public static Action Decide(
        long? localSize,
        long? remoteSize,
        long metaSize,
        DateTime? remoteTime,
        DateTime? metaTime)
    {
        if (localSize == null) return Action.Read;

        // Empty local stub: prefer reading remote (unknown size still means try download)
        if (localSize == 0L && (remoteSize == null || remoteSize > 0L))
            return Action.Read;

        var localChanged = localSize != metaSize;
        var remoteSizeChanged = remoteSize != null && remoteSize != metaSize;
        var remoteTimeNewer = remoteTime != null && metaTime != null && remoteTime > metaTime;
        var remoteChanged = remoteSizeChanged || remoteTimeNewer;

        if (localChanged && remoteChanged)
            return localSize == 0L ? Action.Read : Action.Write;
        if (localChanged) return Action.Write;
        if (remoteChanged) return Action.Read;
        return Action.Skip;
    }
}
