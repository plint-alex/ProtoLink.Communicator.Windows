namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Notes-without-login shell policy (<c>#notes-without-login</c>) + signed-in Messenger main tab
/// (<c>#messenger-startup-priority</c>).
/// Unsigned users keep the tab shell; Messenger / Cloud require login (Settings/Auth).
/// </summary>
public static class AuthShellGate
{
    public const int MessengerTabIndex = 0;
    public const int NotesTabIndex = 1;
    public const int CloudTabIndex = 2;

    /// <summary>
    /// Legacy full-screen AuthFlow over the tab strip is retired — Notes must stay reachable signed-out.
    /// </summary>
    public static bool HideTabsUntilAuthenticated => false;

    public static bool TabRequiresLogin(int tabIndex) =>
        tabIndex is MessengerTabIndex or CloudTabIndex;

    public static bool ShouldPromptLoginForTab(bool isAuthenticated, int tabIndex) =>
        !isAuthenticated && TabRequiresLogin(tabIndex);

    public static int UnsignedDefaultTabIndex => NotesTabIndex;

    /// <summary>Cold start / after login: Messenger is the main signed-in tab (Job #2).</summary>
    public static int SignedInDefaultTabIndex => MessengerTabIndex;

    /// <summary>
    /// Initial tab for a fresh shell: signed-in → Messenger; unsigned → Notes.
    /// Does not bounce intentional Notes/Cloud selection while already authenticated.
    /// </summary>
    public static int ResolveInitialTab(bool isAuthenticated) =>
        isAuthenticated ? SignedInDefaultTabIndex : UnsignedDefaultTabIndex;

    /// <summary>
    /// After logout (or unsigned boot), bounce Messenger/Cloud selection to Notes.
    /// Signed-in users keep their current tab (Notes stays reachable after cold Messenger land).
    /// </summary>
    public static int ResolveTabAfterAuthChange(bool isAuthenticated, int currentTabIndex) =>
        ShouldPromptLoginForTab(isAuthenticated, currentTabIndex)
            ? UnsignedDefaultTabIndex
            : currentTabIndex;
}
