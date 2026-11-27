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
    /// Grantee SWM share root → LeaveSharedWithMe; otherwise owner outbound → DetachFromMyCloud.
    /// </summary>
    public static bool PreferLeaveForMeOnly(bool isSwmShareRoot) => isSwmShareRoot;
}
