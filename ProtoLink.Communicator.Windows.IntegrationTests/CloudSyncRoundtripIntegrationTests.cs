using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Live round-trip: login → create CloudFolder → AddEntity file → DeleteEntity.
/// Skips without PROTOLINK_TEST_PASSWORD. Does not duplicate messenger tests.
/// </summary>
public class CloudSyncRoundtripIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public CloudSyncRoundtripIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Login_CreateFolder_AddAndDeleteFileEntity_Succeeds()
    {
        var password = Environment.GetEnvironmentVariable("PROTOLINK_TEST_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            _output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD");
            return;
        }

        var login = Environment.GetEnvironmentVariable("PROTOLINK_TEST_LOGIN") ?? "alex.plint@gmail.com";
        var baseUrl = (Environment.GetEnvironmentVariable("PROTOLINK_API_BASE") ?? "http://protolink.ru").TrimEnd('/') + "/";
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(120) };

        var loginResp = await http.PostAsJsonAsync("api/Authentication/login", new { login, password });
        var loginBody = await loginResp.Content.ReadAsStringAsync();
        Assert.True(loginResp.IsSuccessStatusCode, loginBody);
        using var loginDoc = JsonDocument.Parse(loginBody);
        var token = loginDoc.RootElement.TryGetProperty("accessToken", out var at)
            ? at.GetString()
            : loginDoc.RootElement.GetProperty("token").GetString();
        var userId = loginDoc.RootElement.TryGetProperty("userId", out var uid)
            ? uid.GetGuid()
            : loginDoc.RootElement.GetProperty("id").GetGuid();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Ensure CloudRoot
        var roots = await http.PostAsJsonAsync("api/Entities/GetEntities", new
        {
            parentIds = new[] { userId },
            includeValues = false,
            take = 200
        });
        var rootsJson = await roots.Content.ReadAsStringAsync();
        Assert.True(roots.IsSuccessStatusCode, rootsJson);
        using var rootsDoc = JsonDocument.Parse(rootsJson);
        Guid cloudRoot;
        var found = rootsDoc.RootElement.EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("code").GetString() == "CloudRoot");
        if (found.ValueKind == JsonValueKind.Object)
            cloudRoot = found.GetProperty("id").GetGuid();
        else
        {
            var addRoot = await http.PostAsJsonAsync("api/Entities/AddEntity", new
            {
                code = "CloudRoot",
                parentIds = new[] { userId }
            });
            var addRootBody = await addRoot.Content.ReadAsStringAsync();
            Assert.True(addRoot.IsSuccessStatusCode, addRootBody);
            cloudRoot = JsonDocument.Parse(addRootBody).RootElement.GetProperty("id").GetGuid();
        }

        var nameType = Guid.Parse("00010003-0000-0000-0000-000000000000");
        var folderName = "sync-it-" + Guid.NewGuid().ToString("N")[..8];
        var addFolder = await http.PostAsJsonAsync("api/Entities/AddEntity", new
        {
            code = "CloudFolder",
            parentIds = new[] { cloudRoot },
            values = new[]
            {
                new { type = 0, value = folderName, parentIds = new[] { nameType } }
            }
        });
        var folderBody = await addFolder.Content.ReadAsStringAsync();
        Assert.True(addFolder.IsSuccessStatusCode, folderBody);
        var folderId = JsonDocument.Parse(folderBody).RootElement.GetProperty("id").GetGuid();

        var fileName = "ping.txt";
        var addFile = await http.PostAsJsonAsync("api/Entities/AddEntity", new
        {
            code = "CloudFile",
            parentIds = new[] { folderId },
            values = new[]
            {
                new { type = 0, value = fileName, parentIds = new[] { nameType } }
            }
        });
        var fileBody = await addFile.Content.ReadAsStringAsync();
        Assert.True(addFile.IsSuccessStatusCode, fileBody);
        var fileId = JsonDocument.Parse(fileBody).RootElement.GetProperty("id").GetGuid();

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(fileId.ToString()), "EntityId");
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("hello-sync")), "File", fileName);
        var upload = await http.PostAsync("api/Files/addFile", content);
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());

        var delFile = await http.PostAsJsonAsync("api/Entities/DeleteEntity", new { id = fileId });
        Assert.True(delFile.IsSuccessStatusCode, await delFile.Content.ReadAsStringAsync());
        var delFolder = await http.PostAsJsonAsync("api/Entities/DeleteEntity", new { id = folderId });
        Assert.True(delFolder.IsSuccessStatusCode, await delFolder.Content.ReadAsStringAsync());
        _output.WriteLine("Round-trip OK for " + folderName);
    }
}
