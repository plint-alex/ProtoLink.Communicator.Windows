namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// Pure helpers for E6 “Add local folder to Cloud…” discoverability (AT-MAP-1/5).
/// </summary>
public static class MapLocalFolderCta
{
    /// <summary>
    /// Hub / writable folder / inside an already-mapped tree (so the user is not stuck with no affordance).
    /// Shared browse-only stays hidden (AT-MAP-5).
    /// </summary>
    public static bool ShouldShow(
        bool isAuthenticated,
        bool showSharedBrowseOnlyHint,
        bool isCloudHubListing,
        bool canMutateListedFolder,
        bool isInsideMappedTree) =>
        isAuthenticated
        && !showSharedBrowseOnlyHint
        && (isCloudHubListing || canMutateListedFolder || isInsideMappedTree);
}
