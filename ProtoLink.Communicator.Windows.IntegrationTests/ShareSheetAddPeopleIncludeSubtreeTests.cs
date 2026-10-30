using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>P1-20261030: AddPermission IncludeSubtree for folder people grants; file stays false.</summary>
[Trait("Category", "Offline")]
public class ShareSheetAddPeopleIncludeSubtreeTests
{
    private static readonly Guid EntityId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid UserId = Guid.Parse("8f3a1111-2222-3333-4444-555555555555");

    [Fact]
    public async Task AddPermissionAsync_IncludeSubtreeTrue_SerializesCamelCaseFlag()
    {
        var body = await CaptureAddPermissionBodyAsync(includeSubtree: true);
        Assert.True(body.GetProperty("includeSubtree").GetBoolean());
        Assert.Equal(EntityId, body.GetProperty("id").GetGuid());
        Assert.Equal(UserId, body.GetProperty("permissionForId").GetGuid());
    }

    [Fact]
    public async Task AddPermissionAsync_Default_SerializesIncludeSubtreeFalse()
    {
        var body = await CaptureAddPermissionBodyAsync(includeSubtree: false);
        Assert.False(body.GetProperty("includeSubtree").GetBoolean());
    }

    [Fact]
    public void AddPermissionContract_DefaultIncludeSubtree_IsFalse()
    {
        var contract = new AddPermissionContract
        {
            Id = EntityId,
            PermissionForId = UserId,
            CanWrite = false
        };
        Assert.False(contract.IncludeSubtree);
    }

    private static async Task<JsonElement> CaptureAddPermissionBodyAsync(bool includeSubtree)
    {
        var handler = new CaptureHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var api = new CloudApiService(http);

        await api.AddPermissionAsync(EntityId, UserId, canWrite: false, includeSubtree: includeSubtree);

        Assert.NotNull(handler.Body);
        return handler.Body!.Value;
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public JsonElement? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var raw = request.Content == null
                ? "{}"
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Body = JsonSerializer.Deserialize<JsonElement>(raw);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }
}
