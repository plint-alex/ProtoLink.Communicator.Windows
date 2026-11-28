namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Chrome-minimal cycle-1: Notes/Cloud create + Download/Open must not live on the
/// permanent shell TabToolbar (FAB / empty / context / entity ⋮ / Info own those jobs).
/// E11a: Notes-tab root Add/Remove/Map may sit adjacent to the Settings gear.
/// CARG1: Cloud-tab Refresh + Add-local may sit adjacent to the Settings gear
/// (overrides prior CM-TT “Cloud empty” for these two only). F5 / context Refresh remain.
/// </summary>
public static class ShellTabToolbarChrome
{
    public const int MessengerTabIndex = 0;
    public const int NotesTabIndex = 1;
    public const int CloudTabIndex = 2;

    public const string NotesAddRootActionId = "notes-add-root";
    public const string NotesRemoveRootActionId = "notes-remove-root";
    public const string NotesMapRootActionId = "notes-map-root";

    public const string CloudRefreshActionId = "cloud-refresh";
    public const string CloudAddLocalActionId = "cloud-add-local";

    private static readonly string[] NotesRootChromeActions =
    {
        NotesAddRootActionId,
        NotesRemoveRootActionId,
        NotesMapRootActionId
    };

    private static readonly string[] CloudChromeActions =
    {
        CloudRefreshActionId,
        CloudAddLocalActionId
    };

    /// <summary>
    /// Permanent TabToolbar actions for a shell tab.
    /// Messenger: empty (CM-TT1…TT4). Notes: E11a root Add/Remove/Map.
    /// Cloud: CARG1 Refresh + Add-local only (not create/Download/Open).
    /// </summary>
    public static IReadOnlyList<string> PermanentActionIds(int selectedTabIndex) =>
        selectedTabIndex switch
        {
            NotesTabIndex => NotesRootChromeActions,
            CloudTabIndex => CloudChromeActions,
            _ => Array.Empty<string>()
        };

    public static bool AllowsPermanentCreateActions(int selectedTabIndex) =>
        PermanentActionIds(selectedTabIndex).Any(static id =>
            id is "new-folder" or "upload" or "add-local");

    public static bool AllowsPermanentDownloadOpenActions(int selectedTabIndex) =>
        PermanentActionIds(selectedTabIndex).Any(static id =>
            id is "download" or "open-in-browser");

    public static bool IsNotesRootChromeAction(string actionId) =>
        actionId is NotesAddRootActionId or NotesRemoveRootActionId or NotesMapRootActionId;

    public static bool IsCloudChromeAction(string actionId) =>
        actionId is CloudRefreshActionId or CloudAddLocalActionId;

    public static bool IsWiredPermanentAction(string actionId) =>
        IsNotesRootChromeAction(actionId) || IsCloudChromeAction(actionId);
}
