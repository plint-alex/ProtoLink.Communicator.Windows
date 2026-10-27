using System.IO;

namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// Cheap Size column for Cloud Explorer details (AT-CE2). No network probes.
/// </summary>
public static class CloudItemSizeCue
{
    public const string Unknown = "—";

    /// <summary>Format known byte length; null/negative → em-dash.</summary>
    public static string Format(long? sizeBytes)
    {
        if (sizeBytes is not >= 0)
            return Unknown;
        return FormatBytes(sizeBytes.Value);
    }

    /// <summary>
    /// Local mirror file length when the path exists; folders / missing → null (display em-dash).
    /// Soft-fails IO — never throws into listing.
    /// </summary>
    public static long? TryReadLocalFileLength(string? fullPath, bool isFolder)
    {
        if (isFolder || string.IsNullOrWhiteSpace(fullPath))
            return null;
        try
        {
            if (!File.Exists(fullPath))
                return null;
            return new FileInfo(fullPath).Length;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        if (unit == 0)
            return $"{bytes} {units[unit]}";
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{size:0.#} {units[unit]}");
    }
}
