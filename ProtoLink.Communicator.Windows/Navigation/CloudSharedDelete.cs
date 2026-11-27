namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// E12 shared Cloud delete: detect multi-principal targets and map me-only vs for-all choices.
/// Nested items under a share stay on the E10 single-confirm path (content delete).
/// </summary>
public enum SharedCloudDeleteChoice
{
    Cancel = 0,
    ForMeOnly = 1,
    ForAll = 2,
}

public static class CloudSharedDelete
{
    /// <summary>
    /// Show me/all for SWM/SBM share roots, known outbound roots, or grant probe hits — not nested SWM children.
    /// </summary>
    public static bool IsSharedDeleteTarget(
        bool isSwmShareRoot,
        bool isKnownOutboundShareRoot,
        bool hasExternalGrants) =>
        isSwmShareRoot || isKnownOutboundShareRoot || hasExternalGrants;

    /// <summary>
    /// AT-SFD-2: ListGrants on an unshared owner folder still returns the caller's own people grant.
    /// Count only principals whose <paramref name="permissionForId"/> differs from the caller.
    /// </summary>
    public static bool HasExternalGrants(
        IEnumerable<Guid>? permissionForIds,
        Guid callerId)
    {
        if (permissionForIds == null || callerId == Guid.Empty)
            return false;

        foreach (var id in permissionForIds)
        {
            if (id != callerId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Grantee inbound SWM share root → LeaveSharedWithMe; otherwise owner outbound → DetachFromMyCloud.
    /// Leave is a no-op for the creator (Left=false, MainParent unchanged) — never prefer Leave
    /// when the target is a known outbound share root or has external grants (AT-SFD-4).
    /// </summary>
    public static bool PreferLeaveForMeOnly(
        bool isSwmShareRoot,
        bool isKnownOutboundShareRoot = false) =>
        isSwmShareRoot && !isKnownOutboundShareRoot;

    /// <summary>
    /// Me-only must Detach unless this is a clear inbound SWM leave.
    /// <paramref name="isInSharedWithMeScope"/> must be crumb/virtual SWM only — not "row appears
    /// in Shared with me list" (that flag alone wrongly forced Leave on owner outbound).
    /// Outbound / external grants always Detach.
    /// </summary>
    public static bool PreferDetachForMeOnly(
        bool isSwmShareRoot,
        bool isInSharedWithMeScope,
        bool isKnownOutboundShareRoot) =>
        isKnownOutboundShareRoot
        || !PreferLeaveForMeOnly(
            isSwmShareRoot: isSwmShareRoot && isInSharedWithMeScope,
            isKnownOutboundShareRoot: false);
}
