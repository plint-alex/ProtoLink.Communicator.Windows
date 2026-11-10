namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Notes-without-login shell policy (<c>#notes-without-login</c>).
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

    /// <summary>
    /// After logout (or unsigned boot), bounce Messenger/Cloud selection to Notes.
    /// </summary>
    public static int ResolveTabAfterAuthChange(bool isAuthenticated, int currentTabIndex) =>
        ShouldPromptLoginForTab(isAuthenticated, currentTabIndex)
            ? UnsignedDefaultTabIndex
            : currentTabIndex;
}
