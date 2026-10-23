using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>AuthService POSTs Bearer changePassword; offline HTTP stub.</summary>
[Trait("Category", "Offline")]
public class AuthServiceChangePasswordTests
{
    [Fact]
    public async Task ChangePasswordAsync_WhenNotSignedIn_FailsWithoutHttp()
    {
        var handler = new RecordingHandler();
        var sut = new AuthService(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") },
            new MemoryTokenService(null), NullLogger<AuthService>.Instance);

        var result = await sut.ChangePasswordAsync("old", "newpass");

        Assert.False(result.Success);
        Assert.Equal("Not signed in.", result.Error);
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task ChangePasswordAsync_PostsBearerAndBody_OnSuccess()
    {
        var handler = new RecordingHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"success":true}""", Encoding.UTF8, "application/json")
            }
        };
        var token = new TokenData
        {
            AccessToken = "access-jwt",
            Login = "u@example.com",
            UserId = Guid.NewGuid()
        };
        var sut = new AuthService(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") },
            new MemoryTokenService(token), NullLogger<AuthService>.Instance);

        var result = await sut.ChangePasswordAsync("old-pass", "new-pass");

        Assert.True(result.Success);
        Assert.Single(handler.Paths);
        Assert.Equal("api/Authentication/changePassword", handler.Paths[0]);
        Assert.Equal("Bearer", handler.AuthScheme);
        Assert.Equal("access-jwt", handler.AuthParameter);
        Assert.Contains("currentPassword", handler.Bodies[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("newPassword", handler.Bodies[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongCurrent_MapsError()
    {
        var handler = new RecordingHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    """{"success":false,"error":"Current password is incorrect."}""",
                    Encoding.UTF8,
                    "application/json")
            }
        };
        var token = new TokenData { AccessToken = "t", Login = "u", UserId = Guid.NewGuid() };
        var sut = new AuthService(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") },
            new MemoryTokenService(token), NullLogger<AuthService>.Instance);

        var result = await sut.ChangePasswordAsync("wrong", "new-pass");

        Assert.False(result.Success);
        Assert.Equal("Current password is incorrect.", result.Error);
    }

    private sealed class MemoryTokenService : ITokenService
    {
        private TokenData? _token;

        public MemoryTokenService(TokenData? token) => _token = token;

        public void SaveToken(TokenData data) => _token = data;
        public TokenData? LoadToken() => _token;
        public void ClearToken() => _token = null;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();
        public List<string> Bodies { get; } = new();
        public string? AuthScheme { get; private set; }
        public string? AuthParameter { get; private set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            Paths.Add(uri == null
                ? ""
                : uri.IsAbsoluteUri
                    ? uri.AbsolutePath.TrimStart('/')
                    : uri.OriginalString.TrimStart('/'));
            AuthScheme = request.Headers.Authorization?.Scheme;
            AuthParameter = request.Headers.Authorization?.Parameter;
            if (request.Content != null)
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            return ResponseFactory?.Invoke(request)
                   ?? new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
    }
}
