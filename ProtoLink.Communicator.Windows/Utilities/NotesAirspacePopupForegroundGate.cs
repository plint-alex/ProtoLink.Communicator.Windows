namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// AT-N12 / E5b: Notes airspace Popups (Folder files, format bar, Share sheet) must not stay
/// desktop-topmost when Communicator is deactivated. While suspended, desired-open state stays
/// true in the VM; only the Popup HWND IsOpen is gated off until foreground resumes.
/// </summary>
public static class NotesAirspacePopupForegroundGate
{
    /// <summary>Whether a Popup HWND should be open given VM/layout desire and background suspend.</summary>
    public static bool ShouldOpenPopup(bool desiredOpen, bool suspendedForBackground) =>
        desiredOpen && !suspendedForBackground;

    /// <summary>
    /// After Suspend, whether FormatBar should reopen on Resume.
    /// Share/sibling restore from live VM flags; format bar has no VM flag so remember was-open.
    /// </summary>
    public static bool ShouldRestoreFormatBar(bool wasOpenWhenSuspended, bool stillHasOpenPage) =>
        wasOpenWhenSuspended && stillHasOpenPage;
}
