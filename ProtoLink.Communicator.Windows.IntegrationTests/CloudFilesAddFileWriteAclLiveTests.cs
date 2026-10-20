using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Live verify: <c>POST api/Files/addFile</c> denies users without write before MinIO replace.
/// Requires LocalStand (or equiv) with Api <c>3c11b7a</c>+, <c>PROTOLINK_API_BASE</c>, <c>PROTOLINK_TEST_PASSWORD</c>.
/// </summary>
[Trait("Category", "Live")]
public sealed class CloudFilesAddFileWriteAclLiveTests
{
    private static readonly Guid NameTypeId = Guid.Parse("00010003-0000-0000-0000-000000000000");

    private readonly ITestOutputHelper _output;

    public CloudFilesAddFileWriteAclLiveTests(ITestOutputHelper output) => _output = output;

    [LiveFact]
    public async Task StrangerAddFile_Returns403_BlobUnchanged_OwnerUploadOk()
    {
        var password = Environment.GetEnvironmentVariable("PROTOLINK_TEST_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            _output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD");
            return;
        }

        var ownerLogin = Environment.GetEnvironmentVariable("PROTOLINK_TEST_LOGIN")
                         ?? MessengerTestCredentials.PrimaryLogin;
        var partnerPassword = Environment.GetEnvironmentVariable("PROTOLINK_TEST_PARTNER_PASSWORD")
                              ?? password;

        using var http = new HttpClient();
        IntegrationTestHttp.ConfigureClient(http);

        var owner = await LoginAsync(http, ownerLogin, password);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);

        Guid? folderId = null;
        Guid? fileId = null;
        try
        {
            var cloudRoot = await EnsureCloudRootAsync(http, owner.UserId);
            var folderName = "addfile-acl-" + Guid.NewGuid().ToString("N")[..8];
            folderId = await AddNamedEntityAsync(http, "CloudFolder", cloudRoot, folderName);

            const string fileName = "owned.bin";
            fileId = await AddNamedEntityAsync(http, "CloudFile", folderId.Value, fileName);

            var original = Encoding.UTF8.GetBytes("original-blob-" + Guid.NewGuid().ToString("N"));
            await UploadAsync(http, fileId.Value, fileName, original, "application/octet-stream");

            var before = await GetFileBytesAsync(http, fileId.Value);
            Assert.Equal(original, before);
            var beforeSha = Convert.ToHexString(SHA256.HashData(before));

            var attacker = Encoding.UTF8.GetBytes("attacker-overwrite-" + Guid.NewGuid().ToString("N"));
            using (var strangerHttp = new HttpClient())
            {
                IntegrationTestHttp.ConfigureClient(strangerHttp);
                var stranger = await LoginAsync(
                    strangerHttp,
                    MessengerTestCredentials.PartnerLogin,
                    partnerPassword);
                strangerHttp.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", stranger.Token);

                using var form = BuildMultipart(fileId.Value, fileName, attacker, "application/octet-stream");
                var deny = await strangerHttp.PostAsync("api/Files/addFile", form);
                Assert.Equal(HttpStatusCode.Forbidden, deny.StatusCode);
                Assert.NotEqual(HttpStatusCode.Unauthorized, deny.StatusCode);
                var denyBody = await deny.Content.ReadAsStringAsync();
                using var denyDoc = JsonDocument.Parse(denyBody);
                Assert.True(denyDoc.RootElement.TryGetProperty("message", out var msg));
                Assert.False(string.IsNullOrWhiteSpace(msg.GetString()));
                Assert.Contains(fileId.Value.ToString("D"), msg.GetString()!, StringComparison.OrdinalIgnoreCase);
            }

            var afterDeny = await GetFileBytesAsync(http, fileId.Value);
            Assert.Equal(original, afterDeny);
            Assert.Equal(beforeSha, Convert.ToHexString(SHA256.HashData(afterDeny)));

            var replacement = Encoding.UTF8.GetBytes("owner-replace-" + Guid.NewGuid().ToString("N"));
            await UploadAsync(http, fileId.Value, fileName, replacement, "text/plain");
            var afterOwner = await GetFileBytesAsync(http, fileId.Value);
            Assert.Equal(replacement, afterOwner);

            _output.WriteLine("addFile write ACL live OK for " + fileId);
        }
        finally
        {
            await BestEffortDeleteAsync(http, fileId);
            await BestEffortDeleteAsync(http, folderId);
        }
    }

    private static MultipartFormDataContent BuildMultipart(
        Guid entityId,
        string fileName,
        byte[] bytes,
        string contentType)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(entityId.ToString("D")), "EntityId");
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(part, "File", fileName);
        return form;
    }

    private static async Task UploadAsync(
        HttpClient http,
        Guid entityId,
        string fileName,
        byte[] bytes,
        string contentType)
    {
        using var form = BuildMultipart(entityId, fileName, bytes, contentType);
        var upload = await http.PostAsync("api/Files/addFile", form);
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());
        Assert.NotEqual(HttpStatusCode.Unauthorized, upload.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, upload.StatusCode);
    }

    private static async Task<byte[]> GetFileBytesAsync(HttpClient http, Guid entityId)
    {
        var resp = await http.GetAsync($"api/Files/getFile/{entityId:D}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await resp.Content.ReadAsByteArrayAsync();
    }

    private static async Task BestEffortDeleteAsync(HttpClient http, Guid? id)
    {
        if (id is not { } entityId)
            return;
        try
        {
            await http.PostAsJsonAsync("api/Entities/DeleteEntity", new { id = entityId });
        }
        catch
        {
            /* best-effort cleanup */
        }
    }

    private static async Task<(string Token, Guid UserId)> LoginAsync(
        HttpClient http,
        string login,
        string password)
    {
        var loginResp = await http.PostAsJsonAsync("api/Authentication/login", new { login, password });
        var loginBody = await IntegrationTestHttp.AssertSuccessAsync(loginResp, "login " + login);
        using var loginDoc = JsonDocument.Parse(loginBody);
        var token = loginDoc.RootElement.TryGetProperty("accessToken", out var at)
            ? at.GetString()
            : loginDoc.RootElement.GetProperty("token").GetString();
        var userId = loginDoc.RootElement.TryGetProperty("userId", out var uid)
            ? uid.GetGuid()
            : loginDoc.RootElement.GetProperty("id").GetGuid();
        Assert.False(string.IsNullOrEmpty(token));
        return (token!, userId);
    }

    private static async Task<Guid> EnsureCloudRootAsync(HttpClient http, Guid userId)
    {
        var roots = await http.PostAsJsonAsync("api/Entities/GetEntities", new
        {
            parentIds = new[] { userId },
            includeValues = false,
            take = 200
        });
        var rootsJson = await IntegrationTestHttp.AssertSuccessAsync(roots, "GetEntities cloud root");
        using var rootsDoc = JsonDocument.Parse(rootsJson);
        var found = rootsDoc.RootElement.EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("code").GetString() == "CloudRoot");
        if (found.ValueKind == JsonValueKind.Object)
            return found.GetProperty("id").GetGuid();

        var addRoot = await http.PostAsJsonAsync("api/Entities/AddEntity", new
        {
            code = "CloudRoot",
            parentIds = new[] { userId }
        });
        var addRootBody = await IntegrationTestHttp.AssertSuccessAsync(addRoot, "AddEntity CloudRoot");
        return JsonDocument.Parse(addRootBody).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> AddNamedEntityAsync(
        HttpClient http,
        string code,
        Guid parentId,
        string name)
    {
        var add = await http.PostAsJsonAsync("api/Entities/AddEntity", new
        {
            code,
            parentIds = new[] { parentId },
            values = new[]
            {
                new { type = 0, value = name, parentIds = new[] { NameTypeId } }
            }
        });
        var body = await IntegrationTestHttp.AssertSuccessAsync(add, "AddEntity " + code);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }
}
