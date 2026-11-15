using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Offline regression for Comm Win HTTP “stream already consumed” (P1-20261115).
/// </summary>
[Trait("Category", "Offline")]
public class HttpContentBufferingTests
{
    [Fact]
    public async Task ReplaceResponseContent_WhenReadThrowsAfterDrain_LeavesByteArrayContent()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ExplodingAfterPartialSerializeContent()
        };

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() =>
            HttpContentBuffering.ReplaceResponseContentAsync(response, CancellationToken.None));
        Assert.Contains("drain-then-fail", thrown.ToString(), StringComparison.Ordinal);

        Assert.IsType<ByteArrayContent>(response.Content);
        // HttpClient.LoadIntoBufferAsync must not re-hit a drained connection stream.
        var buffered = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(buffered);
    }

    [Fact]
    public async Task AuthPlusDevTools_PostWith401Retry_DoesNotThrowStreamConsumed()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var tokens = new FakeTokenService { AccessToken = "bad" };
        var acceptCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var server = Task.Run(async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                using var client = await listener.AcceptTcpClientAsync(acceptCts.Token);
                await using var stream = client.GetStream();
                var buf = new byte[8192];
                var read = await stream.ReadAsync(buf, acceptCts.Token);
                var req = Encoding.ASCII.GetString(buf, 0, read);
                var ok = req.Contains("Authorization: Bearer good", StringComparison.Ordinal);
                var status = ok ? "200 OK" : "401 Unauthorized";
                var body = ok ? "{\"ok\":true}" : "unauthorized";
                var resp =
                    $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.ASCII.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(resp), acceptCts.Token);
            }
        }, acceptCts.Token);

        var auth = new AuthHandler(
            tokens,
            NullLogger<AuthHandler>.Instance,
            onUnauthorized: null,
            tryRefresh: () =>
            {
                tokens.AccessToken = "good";
                return Task.FromResult(true);
            })
        {
            InnerHandler = new DevToolsLoggingHandler(new HttpClientHandler())
        };

        using var http = new HttpClient(auth);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/api/Entities/GetEntities")
        {
            Content = new StringContent("{\"ids\":[]}", Encoding.UTF8, "application/json")
        };

        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ok", text, StringComparison.OrdinalIgnoreCase);
        await server;
        listener.Stop();
    }

    [Fact]
    public async Task DevToolsLoggingHandler_HappyPath_ResponseReadableAfterSend()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var buf = new byte[4096];
            _ = await stream.ReadAsync(buf);
            const string body = "{\"hello\":\"world\"}";
            var resp =
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(resp));
        });

        using var http = new HttpClient(new DevToolsLoggingHandler(new HttpClientHandler()));
        using var response = await http.GetAsync($"http://127.0.0.1:{port}/api/ping");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"hello\":\"world\"}", await response.Content.ReadAsStringAsync());
        await server;
        listener.Stop();
    }

    /// <summary>
    /// Mimics HttpConnectionResponseContent drained mid-buffer: SerializeToStreamAsync writes then throws
    /// before HttpContent._bufferedContent is set, so a naive catch-without-replace leaves content unusable.
    /// </summary>
    private sealed class ExplodingAfterPartialSerializeContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            var chunk = Encoding.UTF8.GetBytes("partial");
            await stream.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            throw new IOException("simulated drain-then-fail during response buffer");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 7;
            return true;
        }
    }

    private sealed class FakeTokenService : ITokenService
    {
        public string AccessToken { get; set; } = "";

        public TokenData? LoadToken() => new TokenData
        {
            AccessToken = AccessToken,
            RefreshToken = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            UserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Login = "t@test",
            ExpirationTime = DateTime.UtcNow.AddHours(1)
        };

        public void SaveToken(TokenData data) => AccessToken = data.AccessToken;
        public void ClearToken() => AccessToken = "";
    }
}
