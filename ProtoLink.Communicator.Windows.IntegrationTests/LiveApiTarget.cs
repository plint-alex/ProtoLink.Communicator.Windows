using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Live tests talk to whatever <c>PROTOLINK_API_BASE</c> names.
/// With no variable set they do not run, and they never fall back to protolink.ru.
/// A production host also needs <c>PROTOLINK_ALLOW_PROD_TESTS=1</c>.
/// </summary>
internal static class LiveApiTarget
{
    public const string NoBaseSkip =
        "Live test skipped: set PROTOLINK_API_BASE (for example http://localhost:5000/). Tests do not default to protolink.ru.";

    public const string InvalidBaseSkip =
        "Live test skipped: PROTOLINK_API_BASE must be an absolute http or https URL.";

    public const string ProductionSkip =
        "Live test skipped: PROTOLINK_API_BASE targets protolink.ru. Set PROTOLINK_ALLOW_PROD_TESTS=1 to opt in.";

    public static string? SkipReason(string? apiBase, string? allowProd)
    {
        if (string.IsNullOrWhiteSpace(apiBase))
            return NoBaseSkip;
        if (!TryNormalize(apiBase, out _, out var uri))
            return InvalidBaseSkip;
        if (IsProductionHost(uri) && !IsProductionOptIn(allowProd))
            return ProductionSkip;
        return null;
    }

    public static string? SkipReasonFromEnvironment() =>
        SkipReason(
            Environment.GetEnvironmentVariable("PROTOLINK_API_BASE"),
            Environment.GetEnvironmentVariable("PROTOLINK_ALLOW_PROD_TESTS"));

    public static bool AllowsCurrentEnvironment() => SkipReasonFromEnvironment() == null;

    public static bool IsProductionOptIn(string? allowProd) =>
        string.Equals(allowProd?.Trim(), "1", StringComparison.Ordinal);

    public static bool IsProductionHost(Uri uri)
    {
        var host = uri.IdnHost;
        if (host.EndsWith(".", StringComparison.Ordinal))
            host = host.TrimEnd('.');
        return host.Equals("protolink.ru", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".protolink.ru", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryNormalize(string apiBase, out string normalized, out Uri uri)
    {
        normalized = "";
        uri = null!;
        var trimmed = apiBase.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
            || parsed.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(parsed.IdnHost))
            return false;

        uri = parsed;
        var authority = parsed.GetLeftPart(UriPartial.Authority);
        var path = parsed.AbsolutePath;
        if (string.IsNullOrEmpty(path) || path == "/")
            normalized = authority.TrimEnd('/') + "/";
        else
            normalized = authority + path.TrimEnd('/') + "/";
        return true;
    }

    public static string RequireBaseUrl()
    {
        var raw = Environment.GetEnvironmentVariable("PROTOLINK_API_BASE");
        var reason = SkipReason(raw, Environment.GetEnvironmentVariable("PROTOLINK_ALLOW_PROD_TESTS"));
        if (reason != null)
            throw new InvalidOperationException(reason);
        if (!TryNormalize(raw!, out var normalized, out _))
            throw new InvalidOperationException(InvalidBaseSkip);
        return normalized;
    }
}

/// <summary>xUnit skip decided at discovery from the current environment. Live tests stay out of a default <c>dotnet test</c>.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        var reason = LiveApiTarget.SkipReasonFromEnvironment();
        if (reason != null)
            Skip = reason;
    }
}
