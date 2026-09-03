using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using Xunit;
using Xunit.Abstractions;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Mirrors <see cref="Services.CloudFolderSynchronizer"/> and <see cref="ViewModels.CloudViewModel"/> GetEntities usage (cloud folder + includeValues).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Cloud")]
public sealed class CloudGetEntitiesIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private HttpClient _client = null!;

    public CloudGetEntitiesIntegrationTests(ITestOutputHelper output) => _output = output;

    public Task InitializeAsync()
    {
        _client = new HttpClient();
        IntegrationTestHttp.ConfigureClient(_client);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Login_CloudRoot_GetFolderChildren_WithValues_Succeeds()
    {
        var login = await LoginAsync();
        Assert.True(string.IsNullOrEmpty(login.Error), login.Error ?? "Login failed");
        Assert.False(string.IsNullOrEmpty(login.AccessToken));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var userId = login.UserId;

        var t0 = await PostGetEntitiesAsync(new GetEntitiesContract
        {
            ParentIds = new[] { userId },
            IncludeValues = false
        });
        Assert.True(t0.IsSuccess, t0.FailureMessage);
        var atUser = t0.Json ?? new List<GetEntitiesResult>();
        var cloudRoot = atUser.FirstOrDefault(e => e.Code == CloudEntityCodes.CloudRoot);
        if (cloudRoot == null)
        {
            var addRoot = await _client.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
            {
                Code = CloudEntityCodes.CloudRoot,
                ParentIds = new[] { userId }
            }, IntegrationTestHttp.JsonOptions);
            var addBody = await IntegrationTestHttp.AssertSuccessAsync(addRoot, "AddEntity CloudRoot");
            var rootDoc = JsonSerializer.Deserialize<JsonElement>(addBody, IntegrationTestHttp.JsonOptions);
            var rootId = rootDoc.GetProperty("id").GetGuid();
            cloudRoot = new GetEntitiesResult { Id = rootId, Code = CloudEntityCodes.CloudRoot };
        }
        _output.WriteLine($"CloudRoot id={cloudRoot!.Id}");

        var t1 = await PostGetEntitiesAsync(new GetEntitiesContract
        {
            ParentIds = new[] { cloudRoot.Id },
            IncludeValues = true
        });
        Assert.True(t1.IsSuccess, t1.FailureMessage);
        Assert.NotNull(t1.Json);
        _output.WriteLine($"Children with values: {t1.Json!.Count}");
    }

    [Fact]
    public async Task GetEntities_WithValues_DoesNotReturn500_ForOptionalMappedFolder()
    {
        var folderIdStr = Environment.GetEnvironmentVariable("PROTOLINK_TEST_CLOUD_FOLDER_ID");
        if (string.IsNullOrWhiteSpace(folderIdStr) || !Guid.TryParse(folderIdStr.Trim(), out var folderId))
        {
            _output.WriteLine("Skip: set PROTOLINK_TEST_CLOUD_FOLDER_ID to a CloudFolder entity id to stress a specific folder.");
            return;
        }

        var login = await LoginAsync();
        Assert.True(string.IsNullOrEmpty(login.Error), login.Error);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var t = await PostGetEntitiesAsync(new GetEntitiesContract
        {
            ParentIds = new[] { folderId },
            IncludeValues = true
        });
        Assert.True(t.IsSuccess, t.FailureMessage);
    }

    private async Task<LoginResult> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync("api/Authentication/login", new LoginContract
        {
            Login = MessengerTestCredentials.PrimaryLogin,
            Password = MessengerTestCredentials.PrimaryPassword
        }, IntegrationTestHttp.JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<LoginResult>(body, IntegrationTestHttp.JsonOptions) ?? new LoginResult { Error = "empty" };
        if (!response.IsSuccessStatusCode && string.IsNullOrEmpty(result.Error))
            result.Error = body;
        return result;
    }

    private async Task<(bool IsSuccess, List<GetEntitiesResult>? Json, string FailureMessage)> PostGetEntitiesAsync(GetEntitiesContract contract)
    {
        var response = await _client.PostAsJsonAsync("api/Entities/GetEntities", contract, IntegrationTestHttp.JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            return (false, null, IntegrationTestHttp.BuildFailureMessage("POST api/Entities/GetEntities", response, body));

        var list = JsonSerializer.Deserialize<List<GetEntitiesResult>>(body, IntegrationTestHttp.JsonOptions);
        return (true, list, "");
    }
}
