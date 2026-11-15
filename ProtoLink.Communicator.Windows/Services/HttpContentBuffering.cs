using System.Net.Http;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// Buffers <see cref="HttpContent"/> into a fresh <see cref="ByteArrayContent"/> so handlers can
/// log or retry without leaving a drained <c>HttpConnectionResponseContent</c> for
/// <see cref="HttpClient"/>'s post-handler <c>LoadIntoBufferAsync</c> (InvalidOperationException:
/// "The stream was already consumed").
/// </summary>
internal static class HttpContentBuffering
{
    /// <summary>
    /// Reads request content once and replaces it with an equivalent byte copy.
    /// On read failure after the stream may already be drained, still replaces with empty content.
    /// </summary>
    public static async Task<byte[]> ReplaceRequestContentAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var old = request.Content;
        if (old == null)
            return Array.Empty<byte>();

        return await ReplaceCoreAsync(
            old,
            replacement => request.Content = replacement,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads response content once and replaces it with an equivalent byte copy.
    /// On read failure after the stream may already be drained, still replaces with empty content
    /// so <see cref="HttpClient"/> buffering cannot re-serialize the connection stream.
    /// </summary>
    public static async Task<byte[]> ReplaceResponseContentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var old = response.Content;
        if (old == null)
            return Array.Empty<byte>();

        return await ReplaceCoreAsync(
            old,
            replacement => response.Content = replacement,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReplaceCoreAsync(
        HttpContent old,
        Action<ByteArrayContent> assign,
        CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            bytes = await old.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Read may have drained HttpConnectionResponseContent / StreamContent without
            // leaving HttpContent._bufferedContent — replace so HttpClient will not re-buffer.
            assign(CopyHeaders(old, Array.Empty<byte>()));
            TryDispose(old);
            throw;
        }

        assign(CopyHeaders(old, bytes));
        TryDispose(old);
        return bytes;
    }

    private static ByteArrayContent CopyHeaders(HttpContent old, byte[] bytes)
    {
        var replacement = new ByteArrayContent(bytes);
        foreach (var header in old.Headers)
            replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return replacement;
    }

    private static void TryDispose(HttpContent content)
    {
        try
        {
            content.Dispose();
        }
        catch
        {
            // Replacement already assigned; ignore dispose races on drained connection streams.
        }
    }
}
