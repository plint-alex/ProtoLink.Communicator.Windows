using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using Xunit;
using Xunit.Abstractions;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Live ACL matrix for Messenger Received peer-parent exemption (Api <c>7a01002</c>).
/// Mirrors <c>MessengerViewModel.GetOrCreateUserContainer</c> / send path parents.
/// </summary>
[Trait("Category", "Live")]
[Trait("Category", "Integration")]
public sealed class MessengerReceivedParentAclLiveTests
{
    private readonly ITestOutputHelper _output;

    public MessengerReceivedParentAclLiveTests(ITestOutputHelper output) => _output = output;

    [LiveFact]
    public async Task ReceivedUnderPeer_Allow_SentSelf_Allow_ArbitraryAndGroupJoin_403()
    {
        var password = Environment.GetEnvironmentVariable("PROTOLINK_TEST_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            _output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD");
            return;
        }

        using var http = new HttpClient();
        IntegrationTestHttp.ConfigureClient(http);

        var login = await LoginAsync(http, MessengerTestCredentials.PrimaryLogin, password);
        Assert.True(string.IsNullOrEmpty(login.Error), login.Error ?? "Login failed");
        Assert.False(string.IsNullOrEmpty(login.AccessToken));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var partnerId = await GetUserByLoginAsync(http, MessengerTestCredentials.PartnerLogin);
        Assert.NotEqual(Guid.Empty, partnerId);

        // --- AC: Sent under caller's own user id (self write grant) ---
        var sentId = await GetOrCreateUserContainerAsync(http, SystemEntities.Sent, login.UserId, "Sent", grantWrite: false);
        Assert.NotEqual(Guid.Empty, sentId);
        _output.WriteLine($"Sent container ok id={sentId:D}");

        // --- AC: AddEntity Received under peer → 200; AddPermission write for peer ---
        var existingReceived = await FindContainerAsync(http, SystemEntities.Received, partnerId);
        var receivedExisted = existingReceived.HasValue;
        var receivedId = await GetOrCreateUserContainerAsync(
            http, SystemEntities.Received, partnerId, "Received", grantWrite: true);
        Assert.NotEqual(Guid.Empty, receivedId);
        _output.WriteLine(
            $"Received under peer ok id={receivedId:D} (existedBefore={receivedExisted}; " +
            "create-without-write covered by EntitiesServiceWriteAccessTests when container already present)");

        // --- AC: AddEntity under arbitrary folder without write → 403 (incl. code Received + non-user) ---
        var foreignFolderId = Guid.NewGuid();
        await AssertForbiddenAsync(
            http,
            "api/Entities/AddEntity",
            new AddEntityContract
            {
                Code = "Received",
                ParentIds = new[] { SystemEntities.Received, foreignFolderId }
            },
            $"AddEntity Received under non-user parent {foreignFolderId:D}");

        await AssertForbiddenAsync(
            http,
            "api/Entities/AddEntity",
            new AddEntityContract
            {
                Code = "row",
                ParentIds = new[] { foreignFolderId }
            },
            $"AddEntity under arbitrary parent {foreignFolderId:D}");

        // --- AC: AddParent into a group the caller cannot write → 403 ---
        var foreignGroupId = Guid.NewGuid();
        await AssertForbiddenAsync(
            http,
            "api/Entities/AddParent",
            new { id = login.UserId, parentId = foreignGroupId },
            $"AddParent user into group {foreignGroupId:D}");
    }

    private static async Task AssertForbiddenAsync(HttpClient http, string path, object body, string step)
    {
        var response = await http.PostAsJsonAsync(path, body, IntegrationTestHttp.JsonOptions);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden,
            IntegrationTestHttp.BuildFailureMessage($"{step} expected 403", response, text));
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<Guid?> FindContainerAsync(HttpClient client, Guid systemParentId, Guid userId)
    {
        var response = await client.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
        {
            ParentIds = new[] { systemParentId, userId }
        }, IntegrationTestHttp.JsonOptions);
        var body = await IntegrationTestHttp.AssertSuccessAsync(response, "GetEntities container probe");
        var results = JsonSerializer.Deserialize<List<GetEntitiesResult>>(body, IntegrationTestHttp.JsonOptions);
        return results?.FirstOrDefault()?.Id;
    }

    private static async Task<Guid> GetOrCreateUserContainerAsync(
        HttpClient client, Guid systemParentId, Guid userId, string code, bool grantWrite)
    {
        var existing = await FindContainerAsync(client, systemParentId, userId);
        if (existing.HasValue)
            return existing.Value;

        var addResponse = await client.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
        {
            Code = code,
            ParentIds = new[] { systemParentId, userId }
        }, IntegrationTestHttp.JsonOptions);
        var addBody = await IntegrationTestHttp.AssertSuccessAsync(addResponse, $"AddEntity {code} container");
        var addResult = JsonSerializer.Deserialize<JsonElement>(addBody, IntegrationTestHttp.JsonOptions);
        var createdId = addResult.GetProperty("id").GetGuid();
        if (grantWrite && createdId != Guid.Empty)
        {
            var permResp = await client.PostAsJsonAsync("api/Entities/AddPermission", new AddPermissionContract
            {
                Id = createdId,
                PermissionForId = userId,
                CanWrite = true
            }, IntegrationTestHttp.JsonOptions);
            await IntegrationTestHttp.AssertSuccessAsync(permResp, $"AddPermission write for {code} container");
        }
        return createdId;
    }

    private static async Task<LoginResult> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync(
            "api/Authentication/login",
            new LoginContract { Login = email, Password = password },
            IntegrationTestHttp.JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<LoginResult>(body, IntegrationTestHttp.JsonOptions)
                     ?? new LoginResult { Error = "empty response" };
        if (!response.IsSuccessStatusCode && string.IsNullOrEmpty(result.Error))
            result.Error = body;
        return result;
    }

    private static async Task<Guid> GetUserByLoginAsync(HttpClient client, string login)
    {
        var encoded = Uri.EscapeDataString(login);
        var response = await client.GetAsync($"api/Authentication/GetUser?login={encoded}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, IntegrationTestHttp.BuildFailureMessage(
            $"GET api/Authentication/GetUser?login={encoded}", response, body));
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetGuid();
    }
}
