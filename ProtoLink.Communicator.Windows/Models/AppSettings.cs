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
    public string? NotesRootPath { get; set; }
    /// <summary>Last open Cloud entity id; restored on next start (AT-C7). Null → Cloud root.</summary>
    public Guid? LastOpenCloudEntityId { get; set; }
}
