namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Resolve paths under the API root when <c>ApiBaseAddress</c> may already end with <c>/api</c>
/// (daily LA: <c>https://protolink.ru/api</c>) or be a host root (<c>http://localhost:5000/</c>).
/// </summary>
public static class ApiBaseAddressPaths
{
    /// <summary>
    /// Build an absolute URI for a resource under the API root (e.g. <c>Version</c> → …/api/Version).
    /// Never produces <c>/api/api/…</c> when the base already ends with <c>/api</c>.
    /// </summary>
    public static Uri BuildUnderApiRoot(string? apiBaseAddress, string relativeUnderApi)
    {
        if (string.IsNullOrWhiteSpace(apiBaseAddress))
            throw new ArgumentException("API base address is required.", nameof(apiBaseAddress));
        if (string.IsNullOrWhiteSpace(relativeUnderApi))
            throw new ArgumentException("Relative path is required.", nameof(relativeUnderApi));

        var rel = relativeUnderApi.Trim().TrimStart('/');
        var trimmed = apiBaseAddress.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            return new Uri(trimmed + "/" + rel);

        return new Uri(trimmed + "/api/" + rel);
    }
}
