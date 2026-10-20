using System.Text.RegularExpressions;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Pure helpers for Share sheet Add people (share-sheet-ux §4).
/// Contacts come from Messenger UserContacts entities; paste-ID uses GetEntity name resolve.
/// </summary>
public static class ShareAddPeople
{
    private static readonly Regex GuidRegex = new(
        @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GuidInPathRegex = new(
        @"/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Parse raw GUID or a pasted /{userId} (or site URL ending in GUID).</summary>
    public static Guid? ParseUserIdInput(string? raw)
    {
        var s = (raw ?? string.Empty).Trim();
        if (s.Length == 0) return null;
        if (GuidRegex.IsMatch(s) && Guid.TryParse(s, out var direct))
            return direct;
        var m = GuidInPathRegex.Match(s);
        if (m.Success && Guid.TryParse(m.Groups[1].Value, out var fromPath))
            return fromPath;
        return null;
    }

    /// <summary>True when the string is only a GUID — must never be shown as a person name.</summary>
    public static bool IsGuidText(string? text)
    {
        var s = (text ?? string.Empty).Trim();
        return s.Length > 0 && GuidRegex.IsMatch(s);
    }

    /// <summary>
    /// Pick a display name from GetEntity fields. Returns null when missing or GUID-shaped.
    /// </summary>
    public static string? PickResolvedDisplayName(string? name, string? code)
    {
        foreach (var candidate in new[] { name, code })
        {
            var t = (candidate ?? string.Empty).Trim();
            if (t.Length == 0 || IsGuidText(t)) continue;
            return t;
        }
        return null;
    }

    /// <summary>Contact row → partner user id (userid:… value or Code GUID).</summary>
    public static Guid? ExtractContactUserId(GetEntitiesResult? contactEntity)
    {
        if (contactEntity == null) return null;
        if (contactEntity.Values != null)
        {
            foreach (var val in contactEntity.Values)
            {
                if (!string.Equals(val.Type, "StringValue", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(val.Type, "String", StringComparison.OrdinalIgnoreCase))
                    continue;
                var s = val.Value?.ToString();
                if (string.IsNullOrEmpty(s) || !s.StartsWith("userid:", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Guid.TryParse(s.AsSpan("userid:".Length), out var uid))
                    return uid;
            }
        }

        if (!string.IsNullOrEmpty(contactEntity.Code) && Guid.TryParse(contactEntity.Code, out var codeUid))
            return codeUid;
        return null;
    }

    /// <summary>Client-side filter over Messenger contacts (no search API).</summary>
    public static IReadOnlyList<T> FilterContacts<T>(
        IEnumerable<T> contacts,
        Func<T, string> nameSelector,
        string? query)
    {
        var list = contacts?.ToList() ?? new List<T>();
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0) return list;
        return list
            .Where(c => (nameSelector(c) ?? string.Empty)
                .Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>People list never includes guest_link principals (UX-S8).</summary>
    public static bool IsPeopleListPrincipal(string? principalKind) =>
        !string.Equals(principalKind, "guest_link", StringComparison.OrdinalIgnoreCase);
}
