using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Shared base URL, JSON options, timeouts, and failure diagnostics for remote integration tests.</summary>
internal static class IntegrationTestHttp
{
    internal static string GetBaseUrl() =>
        (Environment.GetEnvironmentVariable("PROTOLINK_API_BASE") ?? "http://protolink.ru").TrimEnd('/') + "/";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static void ConfigureClient(HttpClient client)
    {
        client.BaseAddress = new Uri(GetBaseUrl());
        var sec = 180;
        if (int.TryParse(Environment.GetEnvironmentVariable("PROTOLINK_TEST_HTTP_TIMEOUT_SEC"), out var envSec))
            sec = Math.Clamp(envSec, 30, 600);
        client.Timeout = TimeSpan.FromSeconds(sec);
    }

    /// <summary>Reads the response body and asserts HTTP success; returns the body string for JSON parsing.</summary>
    internal static async Task<string> AssertSuccessAsync(HttpResponseMessage response, string step)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, BuildFailureMessage(step, response, body));
        return body;
    }

    internal static string BuildFailureMessage(string step, HttpResponseMessage response, string body)
    {
        var sb = new StringBuilder();
        sb.Append(step);
        sb.Append(". HTTP ").Append((int)response.StatusCode);
        if (!string.IsNullOrEmpty(response.ReasonPhrase))
            sb.Append(' ').Append(response.ReasonPhrase);

        var req = response.RequestMessage;
        if (req != null)
        {
            sb.Append(". Request: ").Append(req.Method.Method).Append(' ');
            var path = req.RequestUri?.PathAndQuery;
            if (!string.IsNullOrEmpty(path))
                sb.Append(path);
        }

        AppendHeader(sb, response.Headers, "Server");
        AppendHeader(sb, response.Content.Headers, "Content-Type");
        AppendHeader(sb, response.Headers, "Date");
        AppendHeader(sb, response.Headers, "Request-Id");
        AppendHeader(sb, response.Headers, "X-Request-Id");
        AppendHeader(sb, response.Headers, "X-Powered-By");
        AppendHeader(sb, response.Headers, "X-AspNet-Version");

        sb.Append(". Body: ").Append(FormatErrorBody(body));

        if ((int)response.StatusCode is 503 or 502)
            sb.Append(" (gateway/app pool overload or recycle often yields 502/503 with HTML or empty body)");

        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, HttpHeaders headers, string name)
    {
        if (!headers.TryGetValues(name, out var values))
            return;
        var v = string.Join(", ", values);
        if (!string.IsNullOrEmpty(v))
            sb.Append(". ").Append(name).Append(": ").Append(v);
    }

    /// <summary>Prefer API JSON <c>{ "error": "..." }</c> over raw HTML.</summary>
    internal static string FormatErrorBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "(empty)";

        var trimmed = body.TrimStart();
        if (trimmed.StartsWith("<", StringComparison.Ordinal))
        {
            var title = TryExtractHtmlTitle(body);
            return string.IsNullOrEmpty(title) ? Truncate(body) : $"HTML: {title}";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
                return "API JSON array: " + Truncate(body, 800);
            if (root.ValueKind != JsonValueKind.Object)
                return Truncate(body);

            if (root.TryGetProperty("error", out var err))
            {
                var es = err.ValueKind == JsonValueKind.String ? err.GetString() : err.GetRawText();
                if (root.TryGetProperty("type", out var typeEl))
                {
                    var ts = typeEl.ValueKind == JsonValueKind.String ? typeEl.GetString() : typeEl.GetRawText();
                    if (!string.IsNullOrEmpty(ts))
                        return "API: " + es + " (" + ts + ")";
                }
                return "API: " + es;
            }
            if (root.TryGetProperty("title", out var titleEl))
                return "API: " + (titleEl.ValueKind == JsonValueKind.String ? titleEl.GetString() : titleEl.GetRawText());
            if (root.TryGetProperty("message", out var msgEl))
                return "API: " + (msgEl.ValueKind == JsonValueKind.String ? msgEl.GetString() : msgEl.GetRawText());
        }
        catch (JsonException)
        {
            /* not JSON */
        }
        catch (InvalidOperationException)
        {
            return Truncate(body, 800);
        }

        return Truncate(body);
    }

    private static string? TryExtractHtmlTitle(string html)
    {
        var open = html.IndexOf("<title>", StringComparison.OrdinalIgnoreCase);
        if (open < 0) return null;
        var close = html.IndexOf("</title>", open + 7, StringComparison.OrdinalIgnoreCase);
        if (close < 0) return null;
        var inner = html.Substring(open + 7, close - open - 7).Trim();
        return inner.Length == 0 ? null : Truncate(inner, 200);
    }

    internal static string Truncate(string? s, int max = 500)
    {
        if (string.IsNullOrEmpty(s)) return "(empty)";
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
