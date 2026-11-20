namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Sick-connection UI policy (#comm-win-sick-connection): prefer online when reachable,
/// paint last-good local cache when not, write cache ASAP on success.
/// </summary>
public static class PreferOnlineUiCache
{
    /// <summary>Always attempt Api refresh after a cache paint (cache is never a stale-only mode).</summary>
    public static bool ShouldRefreshOnlineAfterCachePaint => true;

    /// <summary>Paint disk/memory cache before waiting on network when a prior payload exists.</summary>
    public static bool ShouldPaintCacheBeforeOnline(bool hasCachedPayload) => hasCachedPayload;

    /// <summary>Write/refresh local cache immediately when Api returns success (not deferred to idle/shutdown).</summary>
    public static bool ShouldWriteCacheAsap(bool onlineFetchSucceeded) => onlineFetchSucceeded;

    /// <summary>
    /// Soft-fail expected connectivity: keep painted cache / do not blank chrome.
    /// Unexpected failures may still clear or show error paths at the call site.
    /// </summary>
    public static bool ShouldKeepCacheOnSoftFail(bool isExpectedConnectivityFailure, bool hasCachedPayload) =>
        isExpectedConnectivityFailure && hasCachedPayload;

    /// <summary>
    /// Navigate/list start: prefer painting cache over clearing to empty when cache exists.
    /// </summary>
    public static bool ShouldClearChromeForLoad(bool hasCachedPayloadForTarget) => !hasCachedPayloadForTarget;
}
