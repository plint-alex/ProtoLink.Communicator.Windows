namespace ProtoLink.Communicator.Windows.Models;

public class AppSettings
{
    /// <summary>UI theme: <c>Light</c> or <c>Dark</c>.</summary>
    public string Theme { get; set; } = "Light";

    public string ApiBaseAddress { get; set; } = "http://localhost:5000/";
    /// <summary>Public site root for “Open in browser” on the Cloud tab (entity URL is appended).</summary>
    public string PublicSiteBaseUrl { get; set; } = "https://protolink.ru/";
    public string? NotesRootPath { get; set; }

    /// <summary>
    /// When true, sync compares file size and update time at start and chooses upload (write) or download (read).
    /// </summary>
    public bool CompareSizeAndTimeOnSync { get; set; } = true;
}
