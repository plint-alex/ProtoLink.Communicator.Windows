namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// Wide Cloud Explorer column policy (E9 / E9d / E9e). Compact layout collapses the tree separately.
/// </summary>
public static class CloudExplorerColumnLayout
{
    /// <summary>
    /// AT-CE15 / E9d: reserve the Folders column + splitter only when the tree has rows
    /// and the shell is not in compact width mode.
    /// </summary>
    public static bool ShouldReserveTreeColumn(bool isCompactLayout, bool showFolderTree)
        => !isCompactLayout && showFolderTree;

    /// <summary>
    /// AT-CE16 / E9e: when the Folders tree is reserved, stack contents + info in one main pane
    /// (never Tree | List | Entity as three simultaneous columns).
    /// </summary>
    public static bool ShouldStackBrowsePanes(bool isCompactLayout, bool showFolderTree)
        => ShouldReserveTreeColumn(isCompactLayout, showFolderTree);

    /// <summary>
    /// Wide layout always shows exactly two panes: contents|info (empty tree) or tree|combined (populated).
    /// </summary>
    public static int WideVisiblePaneCount(bool showFolderTree)
    {
        _ = showFolderTree; // both states are two panes (AT-CE16)
        return 2;
    }
}
