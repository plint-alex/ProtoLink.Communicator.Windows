using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Live AT-M11 / AT-M12 / AT-M22 / AT-M23 against <em>messenger-media shaped</em> data:
/// A uploads under <c>CloudRoot/Chats/{B}</c> with a peer-read grant on the file only
/// (same shape as <c>MessengerViewModel.MediaSend</c>), then B / C / anon probe ACL.
/// Account C = <see cref="MessengerTestCredentials.DefaultPageLogin"/> — not a party to A↔B.
/// </summary>
[Trait("Category", "Live")]
[Trait("Category", "Integration")]
public sealed class MessengerMediaFeatureAclLiveTests
{
    private static readonly Guid NameTypeId = Guid.Parse("00010003-0000-0000-0000-000000000000");
    private const string AccountCLogin = MessengerTestCredentials.DefaultPageLogin;

    private readonly ITestOutputHelper _output;

    public MessengerMediaFeatureAclLiveTests(ITestOutputHelper output) => _output = output;

    [LiveFact]
    public async Task ChatAttachment_PeerCanRead_StrangerAndAnon_403_PrivateCache()
    {
        var password = Environment.GetEnvironmentVariable("PROTOLINK_TEST_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            _output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD");
            return;
        }

        var partnerPassword = Environment.GetEnvironmentVariable("PROTOLINK_TEST_PARTNER_PASSWORD")
                              ?? password;

        using var httpA = new HttpClient();
        IntegrationTestHttp.ConfigureClient(httpA);

        var ownerLogin = Environment.GetEnvironmentVariable("PROTOLINK_TEST_LOGIN")
                         ?? MessengerTestCredentials.PrimaryLogin;
        var a = await LoginAsync(httpA, ownerLogin, password);
        httpA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", a.Token);

        var partnerId = await GetUserIdByLoginAsync(httpA, MessengerTestCredentials.PartnerLogin);
        Assert.NotEqual(Guid.Empty, partnerId);
        Assert.NotEqual(a.UserId, partnerId);

        Guid? chatsFolderId = null;
        Guid? chatFolderId = null;
        Guid? sentFileId = null;
        Guid? siblingFileId = null;
        try
        {
            var cloudRoot = await EnsureCloudRootAsync(httpA, a.UserId);
            chatsFolderId = await EnsureNamedChildAsync(httpA, cloudRoot, "CloudFolder", "Chats");
            var chatFolderName = "atm-chat-" + partnerId.ToString("N")[..8];
            chatFolderId = await AddNamedEntityAsync(httpA, "CloudFolder", chatsFolderId.Value, chatFolderName);

            // Sibling with NO peer grant — AT-M12.
            siblingFileId = await AddNamedEntityAsync(httpA, "CloudFile", chatFolderId.Value, "sibling-private.bin");
            var siblingBytes = new byte[] { 9, 9, 9 };
            await UploadBytesAsync(httpA, siblingFileId.Value, "sibling-private.bin", "application/octet-stream", siblingBytes);

            // Sent attachment with peer-read at create time (messenger send path).
            const string fileName = "chat-photo.png";
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
            sentFileId = await AddNamedEntityWithPeerReadAsync(
                httpA, chatFolderId.Value, fileName, partnerId);
            await UploadBytesAsync(httpA, sentFileId.Value, fileName, "image/png", png);

            // --- AT-M11: B opens getFile / getImage without no-access ---
            using (var httpB = new HttpClient())
            {
                IntegrationTestHttp.ConfigureClient(httpB);
                var b = await LoginAsync(httpB, MessengerTestCredentials.PartnerLogin, partnerPassword);
                httpB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", b.Token);

                var bFile = await httpB.GetAsync($"api/Files/getFile/{sentFileId.Value:D}");
                Assert.Equal(HttpStatusCode.OK, bFile.StatusCode);
                Assert.Equal(png, await bFile.Content.ReadAsByteArrayAsync());

                var bImage = await httpB.GetAsync($"api/Files/getImage/{sentFileId.Value:D}");
                Assert.Equal(HttpStatusCode.OK, bImage.StatusCode);

                // AT-M12: grant is on the file — B cannot GetEntity the chat folder or sibling file.
                await AssertEntityNoAccessAsync(httpB, chatFolderId.Value);
                await AssertNoAccessAsync(httpB, $"api/Files/getFile/{siblingFileId.Value:D}");
                await AssertEntityNoAccessAsync(httpB, siblingFileId.Value);
            }

            // --- AT-M22: C (neither side) signed in → 403 no_access ---
            using (var httpC = new HttpClient())
            {
                IntegrationTestHttp.ConfigureClient(httpC);
                var c = await LoginAsync(httpC, AccountCLogin, password);
                Assert.NotEqual(a.UserId, c.UserId);
                Assert.NotEqual(partnerId, c.UserId);
                httpC.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", c.Token);
                await AssertNoAccessAsync(httpC, $"api/Files/getFile/{sentFileId.Value:D}");
                await AssertNoAccessAsync(httpC, $"api/Files/getImage/{sentFileId.Value:D}");
            }

            // --- AT-M22: logged out ---
            using (var anon = CreateAnonymousClient())
            {
                await AssertNoAccessAsync(anon, $"api/Files/getFile/{sentFileId.Value:D}");
                await AssertNoAccessAsync(anon, $"api/Files/getImage/{sentFileId.Value:D}");
            }

            // --- AT-M23: private Cache-Control (never public) ---
            var ownerImage = await httpA.GetAsync($"api/Files/getImage/{sentFileId.Value:D}");
            Assert.Equal(HttpStatusCode.OK, ownerImage.StatusCode);
            var cache = ownerImage.Headers.CacheControl?.ToString()
                        ?? string.Join(",", ownerImage.Headers.GetValues("Cache-Control"));
            Assert.Equal("private", cache);
            Assert.DoesNotContain("public", cache, StringComparison.OrdinalIgnoreCase);

            _output.WriteLine(
                $"AT-M11/12/22/23 messenger-feature ACL OK file={sentFileId:D} folder={chatFolderId:D}");
        }
        finally
        {
            foreach (var id in new[] { sentFileId, siblingFileId, chatFolderId })
            {
                if (id is { } fid)
                {
                    try { await httpA.PostAsJsonAsync("api/Entities/DeleteEntity", new { id = fid }); }
                    catch { /* best-effort */ }
                }
            }

            // Leave shared Chats folder; only delete if we created a throwaway name under it.
            // chatsFolderId is the durable "Chats" container — do not delete.
            _ = chatsFolderId;
        }
    }

    private static HttpClient CreateAnonymousClient()
    {
        var client = new HttpClient();
        IntegrationTestHttp.ConfigureClient(client);
        return client;
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

    private static async Task<Guid> GetUserIdByLoginAsync(HttpClient http, string login)
    {
        var encoded = Uri.EscapeDataString(login);
        var response = await http.GetAsync($"api/Authentication/GetUser?login={encoded}");
        var body = await IntegrationTestHttp.AssertSuccessAsync(response, "GetUser " + login);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetGuid();
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

    private static async Task<Guid> EnsureNamedChildAsync(
        HttpClient http,
        Guid parentId,
        string code,
        string name)
    {
        var list = await http.PostAsJsonAsync("api/Entities/GetEntities", new
        {
            parentIds = new[] { parentId },
            includeValues = true,
            take = 200
        });
        var listJson = await IntegrationTestHttp.AssertSuccessAsync(list, "GetEntities under " + parentId);
        using var doc = JsonDocument.Parse(listJson);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (!string.Equals(e.GetProperty("code").GetString(), code, StringComparison.Ordinal))
                continue;
            if (TryGetDisplayName(e) == name)
                return e.GetProperty("id").GetGuid();
        }

        return await AddNamedEntityAsync(http, code, parentId, name);
    }

    private static string? TryGetDisplayName(JsonElement entity)
    {
        if (!entity.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var v in values.EnumerateArray())
        {
            if (!v.TryGetProperty("parentIds", out var pids) || pids.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var pid in pids.EnumerateArray())
            {
                if (pid.GetGuid() == NameTypeId && v.TryGetProperty("value", out var val))
                    return val.GetString();
            }
        }

        return null;
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

    private static async Task<Guid> AddNamedEntityWithPeerReadAsync(
        HttpClient http,
        Guid parentId,
        string name,
        Guid peerUserId)
    {
        var add = await http.PostAsJsonAsync("api/Entities/AddEntity", new
        {
            code = "CloudFile",
            parentIds = new[] { parentId },
            values = new[]
            {
                new { type = 0, value = name, parentIds = new[] { NameTypeId } }
            },
            permissions = new[]
            {
                new { permissionForId = peerUserId, canWrite = false }
            }
        });
        var body = await IntegrationTestHttp.AssertSuccessAsync(add, "AddEntity CloudFile+peer");
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task UploadBytesAsync(
        HttpClient http,
        Guid entityId,
        string fileName,
        string mime,
        byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(entityId.ToString("D")), "EntityId");
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(part, "File", fileName);
        var upload = await http.PostAsync("api/Files/addFile", form);
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());
    }

    private static async Task AssertNoAccessAsync(HttpClient http, string path)
    {
        var resp = await http.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("code", out var code));
        Assert.Equal("no_access", code.GetString());
    }

    private static async Task AssertEntityNoAccessAsync(HttpClient http, Guid entityId)
    {
        var resp = await http.GetAsync($"api/Entities/GetEntity/{entityId:D}");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("code", out var code));
        Assert.Equal("no_access", code.GetString());
    }
}
