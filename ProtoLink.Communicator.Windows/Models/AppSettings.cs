using ProtoLink.Communicator.Windows.Navigation;

namespace ProtoLink.Communicator.Windows.Models;

public class AppSettings
{
    /// <summary>UI theme: <c>Light</c> or <c>Dark</c>.</summary>
    public string Theme { get; set; } = "Light";

    public string ApiBaseAddress { get; set; } = "http://localhost:5000/";
    /// <summary>Public site root for entity links (Copy link / Open in browser); the entity id is appended.</summary>
    public string PublicSiteBaseUrl { get; set; } = EntityLinks.DefaultPublicSiteBaseUrl;
    /// <summary>App UI language (e.g. <c>ru-RU</c>); appended to entity links as <c>?lang=</c> only when set.</summary>
    public string? UiLanguage { get; set; }
    /// <summary>
    /// Legacy single Notes root. Mirrored from <see cref="LastSelectedNotesRootPath"/> on save (E11).
    /// Readers should use <c>NotesRootSettings.ResolveRoots/ResolveSelected</c>.
    /// </summary>
    public string? NotesRootPath { get; set; }

    /// <summary>E11: ordered local Notes folder roots (full paths). Empty → fall back to <see cref="NotesRootPath"/>.</summary>
    public List<string> NotesRootPaths { get; set; } = new();

    /// <summary>E11: last selected Notes root path (must be in <see cref="NotesRootPaths"/> when set).</summary>
    public string? LastSelectedNotesRootPath { get; set; }

    /// <summary>Last open Cloud entity id; restored on next start (AT-C7). Null → Cloud root.</summary>
    public Guid? LastOpenCloudEntityId { get; set; }

    /// <summary>
    /// Messenger media chat-folder cache: key = <c>{ownerUserId:D}:{partnerUserId:D}</c> → CloudFolder id under CloudRoot/Chats.
    /// Cache-first per client-contract §4; never grant this folder to the peer.
    /// </summary>
    public Dictionary<string, Guid> MessengerChatFolderIds { get; set; } = new();
}
