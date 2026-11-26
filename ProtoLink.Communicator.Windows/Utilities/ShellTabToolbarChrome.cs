namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Chrome-minimal cycle-1: Notes/Cloud create + Download/Open must not live on the
/// permanent shell TabToolbar (FAB / empty / context / entity ⋮ / Info own those jobs).
/// E11a: Notes-tab root Add/Remove/Map may sit adjacent to the Settings gear.
/// Listing Refresh stays on the surface (Cloud search row; Notes tree context; F5).
/// </summary>
public static class ShellTabToolbarChrome
{
    public const int MessengerTabIndex = 0;
    public const int NotesTabIndex = 1;
    public const int CloudTabIndex = 2;

    public const string NotesAddRootActionId = "notes-add-root";
    public const string NotesRemoveRootActionId = "notes-remove-root";
    public const string NotesMapRootActionId = "notes-map-root";

    private static readonly string[] NotesRootChromeActions =
    {
        NotesAddRootActionId,
        NotesRemoveRootActionId,
        NotesMapRootActionId
    };

    /// <summary>
    /// Permanent TabToolbar actions for a shell tab.
    /// Messenger/Cloud: empty (CM-TT1…TT4). Notes: E11a root Add/Remove/Map only.
    /// </summary>
    public static IReadOnlyList<string> PermanentActionIds(int selectedTabIndex) =>
        selectedTabIndex == NotesTabIndex ? NotesRootChromeActions : Array.Empty<string>();

    public static bool AllowsPermanentCreateActions(int selectedTabIndex) =>
        PermanentActionIds(selectedTabIndex).Any(static id =>
            id is "new-folder" or "upload" or "add-local");

    public static bool AllowsPermanentDownloadOpenActions(int selectedTabIndex) =>
        PermanentActionIds(selectedTabIndex).Any(static id =>
            id is "download" or "open-in-browser");

    public static bool IsNotesRootChromeAction(string actionId) =>
        actionId is NotesAddRootActionId or NotesRemoveRootActionId or NotesMapRootActionId;
}
