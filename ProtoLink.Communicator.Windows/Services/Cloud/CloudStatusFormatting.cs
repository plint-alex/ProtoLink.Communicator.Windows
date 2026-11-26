using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// Cloud right-panel status chrome: never dump gateway/openresty HTML into StatusText.
/// Soft Ready / last-good listing recovery stay on callers; this only formats status strings.
/// </summary>
internal static class CloudStatusFormatting
{
    public const string WaitingForNetwork = "Waiting for network…";

    private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const int MaxStatusChars = 160;

    /// <summary>True when body looks like an HTML gateway/error page (openresty, IIS, etc.).</summary>
    public static bool LooksLikeHtmlBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        var trimmed = body.AsSpan().TrimStart();
        if (trimmed.StartsWith("<!", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<head", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<body", StringComparison.OrdinalIgnoreCase))
            return true;
        // Gateway pages often cite the server name inside HTML without a leading doctype in logs.
        return body.Contains("openresty", StringComparison.OrdinalIgnoreCase)
               && body.Contains('<')
               && body.Contains('>');
    }

    /// <summary>
    /// Transient HTTP / gateway failures that should soft-fail Cloud chrome
    /// (Waiting for network) instead of dumping status codes + HTML.
    /// </summary>
    public static bool IsTransientGatewayFailure(Exception? ex)
    {
        if (ex == null) return false;
        if (ex is AggregateException agg)
            return agg.Flatten().InnerExceptions.Any(IsTransientGatewayFailure);

        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is not HttpRequestException hre) continue;
            if (IsTransientGatewayStatus(hre.StatusCode)) return true;
            var msg = hre.Message;
            if (LooksLikeHtmlBody(msg)) return true;
            if (MessageHasTransientGatewayCode(msg)) return true;
        }

        return false;
    }

    public static bool IsTransientGatewayStatus(HttpStatusCode? status)
    {
        if (status is null) return false;
        var code = (int)status.Value;
        return code is 408 or 502 or 503 or 504
               || code >= 500;
    }

    /// <summary>
    /// Soft connectivity chrome for Cloud status strip (listing Soft Ready / sync soft-fail).
    /// </summary>
    public static bool ShouldUseWaitingForNetworkStatus(Exception? ex) =>
        ApiExceptionHelper.IsExpectedConnectivityFailure(ex)
        || IsTransientGatewayFailure(ex);

    /// <summary>Sync / AutoPush failure → short human status (no HTML).</summary>
    public static string FormatSyncFailureStatus(Exception ex) =>
        ShouldUseWaitingForNetworkStatus(ex)
            ? WaitingForNetwork
            : "Error: " + SanitizeMessage(ex.Message);

    /// <summary>Force upload/download failure → short human status (no HTML).</summary>
    public static string FormatForceFailureStatus(Exception ex, string verb) =>
        ShouldUseWaitingForNetworkStatus(ex)
            ? WaitingForNetwork
            : $"Force {verb} error: " + SanitizeMessage(ex.Message);

    /// <summary>
    /// Strip Response HTML dumps and collapse whitespace so status never paints gateway pages.
    /// </summary>
    public static string SanitizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "(no details)";

        var text = message;
        var responseIdx = text.IndexOf("Response:", StringComparison.OrdinalIgnoreCase);
        if (responseIdx >= 0)
        {
            var after = text[(responseIdx + "Response:".Length)..].TrimStart();
            if (LooksLikeHtmlBody(after) || LooksLikeHtmlBody(text))
                text = text[..responseIdx].TrimEnd();
        }
        else if (LooksLikeHtmlBody(text))
        {
            text = HtmlTag.Replace(text, " ");
        }

        text = CollapseWhitespace(text);
        if (text.Length > MaxStatusChars)
            text = text[..(MaxStatusChars - 1)].TrimEnd() + "…";
        return string.IsNullOrWhiteSpace(text) ? WaitingForNetwork : text;
    }

    /// <summary>
    /// Detail fragment for <see cref="HttpRequestException"/> — omit HTML gateway bodies.
    /// </summary>
    public static string FormatHttpFailureDetail(HttpStatusCode statusCode, string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "(no response body from server)";
        if (LooksLikeHtmlBody(body) || IsTransientGatewayStatus(statusCode))
            return "(gateway response omitted)";
        var trimmed = CollapseWhitespace(body);
        if (trimmed.Length > 500)
            trimmed = trimmed[..499].TrimEnd() + "…";
        return trimmed;
    }

    private static bool MessageHasTransientGatewayCode(string msg)
    {
        // EnsureSuccess / EnsureSuccessStatusCode style: "HTTP 504 …" or "… 504 (Gateway …)".
        foreach (var code in new[] { "408", "500", "502", "503", "504" })
        {
            if (!msg.Contains(code, StringComparison.Ordinal)) continue;
            if (msg.Contains("HTTP", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Gateway", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("status code", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string CollapseWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);
        var prevSpace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (prevSpace) continue;
                sb.Append(' ');
                prevSpace = true;
            }
            else
            {
                sb.Append(ch);
                prevSpace = false;
            }
        }

        return sb.ToString().Trim();
    }
}
