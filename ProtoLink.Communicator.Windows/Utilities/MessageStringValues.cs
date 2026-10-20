namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Shared messenger string-value parse (client-contract §1–2).
/// Sole place that matches the <c>attachment:</c> prefix — migration surface for FileValue later.
/// </summary>
public static class MessageStringValues
{
    public const string AttachmentPrefix = "attachment:";

    /// <summary>
    /// Parse message string values into body text and ordered attachment entity ids.
    /// Skip list (case-sensitive, prefix-only): sender:, receiver:, status:, attachment:.
    /// Malformed <c>attachment:</c> values (non-GUID) are skipped, never treated as body text.
    /// </summary>
    public static (string BodyText, IReadOnlyList<Guid> AttachmentIds) Parse(IEnumerable<string?> stringValues)
    {
        ArgumentNullException.ThrowIfNull(stringValues);

        var attachmentIds = new List<Guid>();
        string? body = null;

        foreach (var raw in stringValues)
        {
            if (string.IsNullOrEmpty(raw))
                continue;

            if (raw.StartsWith("sender:", StringComparison.Ordinal)
                || raw.StartsWith("receiver:", StringComparison.Ordinal)
                || raw.StartsWith("status:", StringComparison.Ordinal))
                continue;

            if (raw.StartsWith(AttachmentPrefix, StringComparison.Ordinal))
            {
                var idPart = raw.AsSpan(AttachmentPrefix.Length);
                if (Guid.TryParse(idPart, out var id))
                    attachmentIds.Add(id);
                continue;
            }

            // One body string is normal; last non-metadata wins (same as prior Windows ParseMessage).
            body = raw;
        }

        return (body ?? string.Empty, attachmentIds);
    }
}
