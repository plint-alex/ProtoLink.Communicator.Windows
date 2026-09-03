using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// Logs outgoing HTTP requests (method, URL, status, duration, bodies, headers) to <see cref="DevToolsStore"/>.
/// Buffers request/response content so streams remain valid for the rest of the pipeline.
/// </summary>
public sealed class DevToolsLoggingHandler : DelegatingHandler
{
    private const int MaxBodyChars = 512 * 1024;

    public DevToolsLoggingHandler(HttpMessageHandler innerHandler) : base(innerHandler)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.Method.Method;
        var url = request.RequestUri?.ToString() ?? "";
        var sw = Stopwatch.StartNew();

        string? requestPreview = null;
        try
        {
            requestPreview = await BufferRequestContentForLoggingAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            requestPreview = $"[could not read request: {ex.Message}]";
        }

        var requestHeadersText = FormatRequestHeaders(request);

        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            var code = (int)response.StatusCode;

            string? responsePreview = null;
            try
            {
                responsePreview = await BufferResponseContentForLoggingAsync(response, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                responsePreview = $"[could not read response: {ex.Message}]";
            }

            var responseHeadersText = FormatResponseHeaders(response);

            DevToolsStore.AppendNetwork(
                method,
                url,
                code,
                sw.ElapsedMilliseconds,
                requestPreview,
                responsePreview,
                requestHeadersText,
                responseHeadersText);
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            DevToolsStore.AppendNetwork(
                method,
                url,
                null,
                sw.ElapsedMilliseconds,
                requestPreview,
                null,
                requestHeadersText,
                null,
                ex.Message);
            throw;
        }
    }

    private static string FormatRequestHeaders(HttpRequestMessage request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{request.Method} {request.RequestUri} HTTP/{request.Version}");
        AppendHeaders(sb, request.Headers);
        if (request.Content != null)
            AppendHeaders(sb, request.Content.Headers);
        return sb.ToString().TrimEnd();
    }

    private static string FormatResponseHeaders(HttpResponseMessage response)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"HTTP/{response.Version} {(int)response.StatusCode} {response.ReasonPhrase}");
        AppendHeaders(sb, response.Headers);
        if (response.Content != null)
            AppendHeaders(sb, response.Content.Headers);
        return sb.ToString().TrimEnd();
    }

    private static void AppendHeaders(StringBuilder sb, HttpHeaders headers)
    {
        foreach (var kv in headers.OrderBy(x => x.Key))
        {
            foreach (var v in kv.Value)
                sb.AppendLine($"{kv.Key}: {v}");
        }
    }

    private static async Task<string?> BufferRequestContentForLoggingAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content == null)
            return null;

        var old = request.Content;
        var bytes = await old.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var preview = TruncateForDevTools(Encoding.UTF8.GetString(bytes));

        var replacement = new ByteArrayContent(bytes);
        foreach (var header in old.Headers)
            replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);

        old.Dispose();
        request.Content = replacement;
        return string.IsNullOrEmpty(preview) ? null : preview;
    }

    private static async Task<string?> BufferResponseContentForLoggingAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content == null)
            return null;

        var old = response.Content;
        var bytes = await old.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var preview = TruncateForDevTools(Encoding.UTF8.GetString(bytes));

        var replacement = new ByteArrayContent(bytes);
        foreach (var header in old.Headers)
            replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);

        old.Dispose();
        response.Content = replacement;
        return string.IsNullOrEmpty(preview) ? null : preview;
    }

    private static string? TruncateForDevTools(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        if (text.Length <= MaxBodyChars)
            return text;
        return text[..MaxBodyChars] + "\n\n… [truncated; " + text.Length + " characters total]";
    }
}
