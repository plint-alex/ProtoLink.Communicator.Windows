using System.Text.RegularExpressions;

namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// M8 — detect ProtoLink entity URLs in message body text. Only <c>{PublicSiteBaseUrl}/{guid}</c>
/// (optional trailing slug / <c>?lang=</c>) is unfurled; external URLs are ignored.
/// </summary>
public static class EntityLinkUnfurl
{
    /// <summary>
    /// Returns distinct entity ids found as ProtoLink public links under <paramref name="publicSiteBaseUrl"/>.
    /// Order = first occurrence in <paramref name="text"/>.
    /// </summary>
    public static IReadOnlyList<Guid> FindEntityIds(string? text, string? publicSiteBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<Guid>();

        var baseUrl = NormalizeBase(publicSiteBaseUrl);
        if (baseUrl.Length == 0)
            return Array.Empty<Guid>();

        // base + / + guid + optional /slug + optional ?query (mid-text)
        var urlPattern = Regex.Escape(baseUrl) + "/(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})"
                         + "(?:/[^\\s?#]*)?(?:\\?[^\\s]*)?";

        var seen = new HashSet<Guid>();
        var ordered = new List<Guid>();
        foreach (Match m in Regex.Matches(text, urlPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (!Guid.TryParse(m.Groups["id"].Value, out var id))
                continue;
            if (seen.Add(id))
                ordered.Add(id);
        }

        return ordered;
    }

    /// <summary>True when <paramref name="url"/> is exactly a ProtoLink entity link for the configured base (AT-M21 external = false).</summary>
    public static bool TryParseEntityUrl(string? url, string? publicSiteBaseUrl, out Guid entityId)
    {
        entityId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var baseUrl = NormalizeBase(publicSiteBaseUrl);
        if (baseUrl.Length == 0)
            return false;

        var trimmed = url.Trim();
        var prefix = baseUrl + "/";
        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = trimmed[prefix.Length..];
        var path = rest.Split('?', 2)[0];
        var firstSeg = path.Split('/', 2)[0];
        return Guid.TryParse(firstSeg, out entityId);
    }

    private static string NormalizeBase(string? publicSiteBaseUrl)
    {
        var raw = string.IsNullOrWhiteSpace(publicSiteBaseUrl)
            ? Navigation.EntityLinks.DefaultPublicSiteBaseUrl
            : publicSiteBaseUrl.Trim();
        return raw.TrimEnd('/');
    }
}
