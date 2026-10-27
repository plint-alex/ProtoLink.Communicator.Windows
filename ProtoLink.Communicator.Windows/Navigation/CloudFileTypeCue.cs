using System.IO;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// Mime / extension → Cloud list type cue (AT-UL3). Offline-pure; no API.
/// Prefer extension short label; fall back to mime family or "File".
/// </summary>
public static class CloudFileTypeCue
{
    public static string ShortLabel(string? mimeType, string? fileName)
    {
        var ext = ExtensionLabel(fileName);
        if (ext != null)
            return ext;

        if (string.IsNullOrWhiteSpace(mimeType))
            return "File";

        var mime = mimeType.Trim();
        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return "Image";
        if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return "Video";
        if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return "Audio";
        if (mime.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            return "PDF";
        if (mime.Equals("application/zip", StringComparison.OrdinalIgnoreCase)
            || mime.Equals("application/x-zip-compressed", StringComparison.OrdinalIgnoreCase))
            return "ZIP";
        if (mime.Equals("application/json", StringComparison.OrdinalIgnoreCase))
            return "JSON";
        if (mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return "Text";

        var slash = mime.LastIndexOf('/');
        if (slash >= 0 && slash < mime.Length - 1)
        {
            var sub = mime[(slash + 1)..];
            var plus = sub.IndexOf('+');
            if (plus > 0)
                sub = sub[..plus];
            if (sub.Length is > 0 and <= 12
                && !sub.Equals("octet-stream", StringComparison.OrdinalIgnoreCase))
                return sub.ToUpperInvariant();
        }

        return "File";
    }

    public static string TypeGlyph(string? mimeType, string? fileName)
    {
        if (MessengerAttachmentRender.LooksLikeImageMime(mimeType) || LooksLikeImageExtension(fileName))
            return "🖼";
        return "📄";
    }

    private static string? ExtensionLabel(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;
        var ext = Path.GetExtension(fileName.Trim());
        if (string.IsNullOrEmpty(ext) || ext.Length < 2)
            return null;
        var label = ext[1..];
        if (label.Length > 8)
            return null;
        return label.ToUpperInvariant();
    }

    private static bool LooksLikeImageExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;
        var ext = Path.GetExtension(fileName.Trim());
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
    }
}
