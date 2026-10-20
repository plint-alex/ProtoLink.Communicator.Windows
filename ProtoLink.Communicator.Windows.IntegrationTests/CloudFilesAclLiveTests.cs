using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Live AT-S30 / AT-S31: getFile/getImage read ACL + GetImage Cache-Control.
/// Requires <c>PROTOLINK_API_BASE</c> pointing at a build with Api <c>3217203</c>+,
/// and <c>PROTOLINK_TEST_PASSWORD</c>. Stranger path uses PartnerLogin with
/// <c>PROTOLINK_TEST_PARTNER_PASSWORD</c> or the same <c>PROTOLINK_TEST_PASSWORD</c>.
/// </summary>
[Trait("Category", "Live")]
public sealed class CloudFilesAclLiveTests
{
    private static readonly Guid NameTypeId = Guid.Parse("00010003-0000-0000-0000-000000000000");
    private static readonly Guid PublicPrincipalId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly ITestOutputHelper _output;

    public CloudFilesAclLiveTests(ITestOutputHelper output) => _output = output;

    [LiveFact]
    public async Task PrivateCloudFile_GetFileGetImage_AclAndCacheControl()
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
            var folderName = "acl-it-" + Guid.NewGuid().ToString("N")[..8];
            folderId = await AddNamedEntityAsync(http, "CloudFolder", cloudRoot, folderName);

            const string fileName = "private-acl.png";
            fileId = await AddNamedEntityAsync(http, "CloudFile", folderId.Value, fileName);

            // Minimal PNG (1×1) so getImage has a real image payload.
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
            using (var form = new MultipartFormDataContent())
            {
                form.Add(new StringContent(fileId.Value.ToString("D")), "EntityId");
                var part = new ByteArrayContent(png);
                part.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                form.Add(part, "File", fileName);
                var upload = await http.PostAsync("api/Files/addFile", form);
                Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());
            }

            // --- AT-S30: anonymous deny ---
            using (var anon = CreateAnonymousClient())
            {
                await AssertNoAccessAsync(anon, $"api/Files/getFile/{fileId.Value:D}");
                await AssertNoAccessAsync(anon, $"api/Files/getImage/{fileId.Value:D}");
            }

            // --- AT-S30: signed-in stranger deny ---
            using (var strangerHttp = new HttpClient())
            {
                IntegrationTestHttp.ConfigureClient(strangerHttp);
                var stranger = await LoginAsync(
                    strangerHttp,
                    MessengerTestCredentials.PartnerLogin,
                    partnerPassword);
                strangerHttp.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", stranger.Token);
                await AssertNoAccessAsync(strangerHttp, $"api/Files/getFile/{fileId.Value:D}");
                await AssertNoAccessAsync(strangerHttp, $"api/Files/getImage/{fileId.Value:D}");
            }

            // --- AT-S30: owner allow ---
            var ownerFile = await http.GetAsync($"api/Files/getFile/{fileId.Value:D}");
            Assert.Equal(HttpStatusCode.OK, ownerFile.StatusCode);
            Assert.NotEqual(HttpStatusCode.Unauthorized, ownerFile.StatusCode);
            var ownerBytes = await ownerFile.Content.ReadAsByteArrayAsync();
            Assert.Equal(png, ownerBytes);

            // --- AT-S31: private Cache-Control ---
            var ownerImage = await http.GetAsync($"api/Files/getImage/{fileId.Value:D}");
            Assert.Equal(HttpStatusCode.OK, ownerImage.StatusCode);
            var cachePrivate = ownerImage.Headers.CacheControl?.ToString()
                               ?? string.Join(",", ownerImage.Headers.GetValues("Cache-Control"));
            Assert.Equal("private", cachePrivate);

            // --- Unknown id → 404 not_found (never 403) ---
            var missing = Guid.NewGuid();
            using (var anon = CreateAnonymousClient())
            {
                await AssertNotFoundAsync(anon, $"api/Files/getFile/{missing:D}");
                await AssertNotFoundAsync(anon, $"api/Files/getImage/{missing:D}");
            }

            // --- AT-S31: public grant → Cache-Control public ---
            var addPerm = await http.PostAsJsonAsync("api/Entities/AddPermission", new
            {
                id = fileId.Value,
                permissionForId = PublicPrincipalId,
                canWrite = false
            });
            Assert.True(addPerm.IsSuccessStatusCode, await addPerm.Content.ReadAsStringAsync());

            using (var anon = CreateAnonymousClient())
            {
                var publicImage = await anon.GetAsync($"api/Files/getImage/{fileId.Value:D}");
                Assert.Equal(HttpStatusCode.OK, publicImage.StatusCode);
                var cachePublic = publicImage.Headers.CacheControl?.ToString()
                                  ?? string.Join(",", publicImage.Headers.GetValues("Cache-Control"));
                Assert.Equal("public, max-age=86400", cachePublic);

                var publicFile = await anon.GetAsync($"api/Files/getFile/{fileId.Value:D}");
                Assert.Equal(HttpStatusCode.OK, publicFile.StatusCode);
            }

            _output.WriteLine("AT-S30/S31 live OK for " + fileId);
        }
        finally
        {
            if (fileId is { } fid)
            {
                try
                {
                    await http.PostAsJsonAsync("api/Entities/DeleteEntity", new { id = fid });
                }
                catch { /* best-effort cleanup */ }
            }

            if (folderId is { } fold)
            {
                try
                {
                    await http.PostAsJsonAsync("api/Entities/DeleteEntity", new { id = fold });
                }
                catch { /* best-effort cleanup */ }
            }
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

    private static async Task AssertNotFoundAsync(HttpClient http, string path)
    {
        var resp = await http.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("code", out var code));
        Assert.Equal("not_found", code.GetString());
    }
}
