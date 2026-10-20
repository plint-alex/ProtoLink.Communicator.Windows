namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Mime → bubble kind (client-contract §6 / M6). Image only after getImage returns image bytes;
/// "Send as file" forces file row on the sender without changing stored mime.
/// </summary>
public enum MessengerAttachmentVisualKind
{
    Loading,
    Image,
    File,
    TombstoneRemoved,
    TombstoneNoAccess
}

public static class MessengerAttachmentRender
{
    public const string TombstoneRemovedEn = "File removed";
    public const string TombstoneNoAccessEn = "You don't have access to this item";
    public const string CapRefuseEn = "Up to 10 files per message";
    public const string DropOverlayEn = "Drop files to attach";

    public static bool LooksLikeImageMime(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType)) return false;
        return mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Initial kind before network probe. <paramref name="forceFileRow"/> is the composer "Send as file" flag (not on the wire).
    /// </summary>
    public static MessengerAttachmentVisualKind InitialKind(string? mimeType, bool forceFileRow)
    {
        if (forceFileRow) return MessengerAttachmentVisualKind.File;
        if (LooksLikeImageMime(mimeType)) return MessengerAttachmentVisualKind.Loading;
        return MessengerAttachmentVisualKind.File;
    }

    /// <summary>
    /// After getImage (or getFile probe). Non-image content-type or decode failure → file row (AT-M20).
    /// </summary>
    public static MessengerAttachmentVisualKind AfterImageProbe(
        bool forceFileRow,
        int? httpStatus,
        string? contentType,
        bool decodedAsImage)
    {
        if (forceFileRow)
            return MessengerAttachmentVisualKind.File;

        if (httpStatus == 404)
            return MessengerAttachmentVisualKind.TombstoneRemoved;
        if (httpStatus == 403)
            return MessengerAttachmentVisualKind.TombstoneNoAccess;

        if (httpStatus is >= 200 and < 300 && decodedAsImage && IsImageContentType(contentType))
            return MessengerAttachmentVisualKind.Image;

        // Mislabelled mime / non-image payload (AT-M20), or other readable errors → file row.
        return MessengerAttachmentVisualKind.File;
    }

    public static bool IsImageContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        var ct = contentType;
        var semi = ct.IndexOf(';');
        if (semi >= 0) ct = ct[..semi];
        ct = ct.Trim();
        return ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Split an attachment list into batches of at most <paramref name="maxPerBatch"/> (AT-M10).</summary>
    public static IReadOnlyList<IReadOnlyList<T>> SplitBatches<T>(IReadOnlyList<T> items, int maxPerBatch)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (maxPerBatch <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPerBatch));

        if (items.Count == 0)
            return Array.Empty<IReadOnlyList<T>>();

        var batches = new List<IReadOnlyList<T>>();
        for (var i = 0; i < items.Count; i += maxPerBatch)
        {
            var take = Math.Min(maxPerBatch, items.Count - i);
            var batch = new T[take];
            for (var j = 0; j < take; j++)
                batch[j] = items[i + j];
            batches.Add(batch);
        }

        return batches;
    }

    public static string FormatByteSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024)
            return kb >= 100
                ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{kb:0} KB")
                : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{kb:0.#} KB");
        double mb = kb / 1024.0;
        if (mb >= 10)
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{mb:0.0} MB");
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{mb:0.##} MB");
    }

    public static string ScreenshotFileName(DateTime localNow) =>
        $"screenshot-{localNow:yyyy-MM-dd-HHmm}.png";
}
