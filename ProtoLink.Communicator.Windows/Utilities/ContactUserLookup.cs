using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Classify / resolve Add-contact input as Guid or email/login (Auth GetUser / GetUserById).
/// Mirrors ViewEditor contacts-app-view classifyUserLookup + resolveUser.
/// </summary>
public static class ContactUserLookup
{
    private static readonly Regex UserGuidRegex = new(
        @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public enum Kind
    {
        Empty,
        ById,
        ByLogin
    }

    public readonly record struct Lookup(Kind Kind, string Value);

    public static Lookup Classify(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0)
            return new Lookup(Kind.Empty, string.Empty);
        if (UserGuidRegex.IsMatch(value))
            return new Lookup(Kind.ById, value);
        return new Lookup(Kind.ByLogin, value);
    }

    /// <summary>
    /// Resolve to user id via Auth. Returns null when empty/invalid/not found.
    /// </summary>
    public static async Task<Guid?> ResolveUserIdAsync(HttpClient http, string? raw, CancellationToken ct = default)
    {
        var lookup = Classify(raw);
        if (lookup.Kind == Kind.Empty)
            return null;

        string path;
        if (lookup.Kind == Kind.ById)
            path = $"api/Authentication/GetUserById?userId={Uri.EscapeDataString(lookup.Value)}";
        else
            path = $"api/Authentication/GetUser?login={Uri.EscapeDataString(lookup.Value)}";

        using var response = await http.GetAsync(path, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;
        if (!TryReadGuid(root, "id", out var id)
            && !TryReadGuid(root, "Id", out id)
            && !TryReadGuid(root, "userId", out id)
            && !TryReadGuid(root, "UserId", out id))
            return null;
        return id;
    }

    private static bool TryReadGuid(JsonElement root, string name, out Guid id)
    {
        id = Guid.Empty;
        if (!root.TryGetProperty(name, out var el))
            return false;
        if (el.ValueKind == JsonValueKind.String
            && Guid.TryParse(el.GetString(), out id)
            && id != Guid.Empty)
            return true;
        return false;
    }
}
