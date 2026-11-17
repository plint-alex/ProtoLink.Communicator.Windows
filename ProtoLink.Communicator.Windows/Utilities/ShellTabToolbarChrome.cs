namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Chrome-minimal cycle-1: Notes/Cloud create + Download/Open must not live on the
/// permanent shell TabToolbar (FAB / empty / context / entity ⋮ / Info own those jobs).
/// Listing Refresh stays on the surface (Cloud search row; Notes tree context; F5).
/// </summary>
public static class ShellTabToolbarChrome
{
    public const int MessengerTabIndex = 0;
    public const int NotesTabIndex = 1;
    public const int CloudTabIndex = 2;

    /// <summary>
    /// Permanent TabToolbar actions for a shell tab. Empty for all current tabs —
    /// Messenger has none; Notes/Cloud create/actions were collapsed (CM-TT1…TT4).
    /// </summary>
    public static IReadOnlyList<string> PermanentActionIds(int selectedTabIndex)
    {
        _ = selectedTabIndex;
        return Array.Empty<string>();
    }

    public static bool AllowsPermanentCreateActions(int selectedTabIndex) =>
        PermanentActionIds(selectedTabIndex).Any(static id =>
            id is "new-folder" or "upload" or "add-local");

    public static bool AllowsPermanentDownloadOpenActions(int selectedTabIndex) =>
        PermanentActionIds(selectedTabIndex).Any(static id =>
            id is "download" or "open-in-browser");
}
