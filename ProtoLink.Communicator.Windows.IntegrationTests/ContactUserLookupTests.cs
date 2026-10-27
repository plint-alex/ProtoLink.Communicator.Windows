using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class ContactUserLookupTests
{
    private static readonly Guid Tina = Guid.Parse("3a9a71a3-8c00-4b68-bf41-99f4a12245f1");

    [Fact]
    public void Classify_Empty_Guid_Email()
    {
        Assert.Equal(ContactUserLookup.Kind.Empty, ContactUserLookup.Classify("  ").Kind);
        Assert.Equal(ContactUserLookup.Kind.ById, ContactUserLookup.Classify(Tina.ToString("D")).Kind);
        Assert.Equal(ContactUserLookup.Kind.ByLogin, ContactUserLookup.Classify("molina.tina@ya.ru").Kind);
    }

    [Fact]
    public async Task Resolve_ByGuid_UsesGetUserById()
    {
        var handler = new AuthLookupHandler(Tina, expectPathContains: "GetUserById");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var id = await ContactUserLookup.ResolveUserIdAsync(http, Tina.ToString("D"));
        Assert.Equal(Tina, id);
        Assert.Contains(handler.Paths, p => p.Contains("GetUserById", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Resolve_ByEmail_UsesGetUser()
    {
        var handler = new AuthLookupHandler(Tina, expectPathContains: "GetUser?");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var id = await ContactUserLookup.ResolveUserIdAsync(http, "molina.tina@ya.ru");
        Assert.Equal(Tina, id);
        Assert.Contains(handler.Paths, p =>
            p.Contains("GetUser?login=", StringComparison.OrdinalIgnoreCase)
            && p.Contains(Uri.EscapeDataString("molina.tina@ya.ru"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resolve_NotFound_ReturnsNull()
    {
        var handler = new AuthLookupHandler(userId: null);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        Assert.Null(await ContactUserLookup.ResolveUserIdAsync(http, "missing@example.com"));
    }

    private sealed class AuthLookupHandler : HttpMessageHandler
    {
        private readonly Guid? _userId;
        public List<string> Paths { get; } = new();

        public AuthLookupHandler(Guid? userId, string? expectPathContains = null)
        {
            _userId = userId;
            _ = expectPathContains;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery ?? "";
            Paths.Add(path);
            if (_userId is not { } id)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                });
            }

            var json = JsonSerializer.Serialize(new { id = id.ToString("D"), login = "molina.tina@ya.ru" });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
