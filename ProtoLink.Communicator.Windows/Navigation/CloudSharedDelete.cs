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
    /// Grantee SWM share root → LeaveSharedWithMe; otherwise owner outbound → DetachFromMyCloud.
    /// </summary>
    public static bool PreferLeaveForMeOnly(bool isSwmShareRoot) => isSwmShareRoot;
}
