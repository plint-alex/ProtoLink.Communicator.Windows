namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// Wide Cloud Explorer column policy (E9 / E9d). Compact layout collapses the tree separately.
/// </summary>
public static class CloudExplorerColumnLayout
{
    /// <summary>
    /// AT-CE15 / E9d: reserve the Folders column + splitter only when the tree has rows
    /// and the shell is not in compact width mode.
    /// </summary>
    public static bool ShouldReserveTreeColumn(bool isCompactLayout, bool showFolderTree)
        => !isCompactLayout && showFolderTree;
}
