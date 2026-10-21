namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>When the Cloud list Pattern 1 create FAB is shown (cycle1-next-polish UX-C-F1/F3/F4).</summary>
public static class CloudCreateFabVisibility
{
    /// <summary>
    /// FAB when the listed folder is mutable and has items.
    /// Hidden for hub / Shared with me / other virtual nodes, and when empty
    /// so the centered empty-state Upload stays the sole primary create control.
    /// </summary>
    public static bool IsVisible(bool canMutateListedFolder, bool isEmpty) =>
        canMutateListedFolder && !isEmpty;
}
