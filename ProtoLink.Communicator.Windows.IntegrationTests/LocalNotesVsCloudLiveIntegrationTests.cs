using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Sync;
using Xunit;
using Xunit.Abstractions;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Live compare: local Notes file vs cloud blob using Windows Communicator LocalAppData
/// (settings, sync mappings, sync metadata, token.dat).
///
/// Default path: Films/index.html under mapped MyNotesFolder.
/// Override with PROTOLINK_COMPARE_REL_PATH (e.g. notes/index.html).
/// </summary>
public class LocalNotesVsCloudLiveIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public LocalNotesVsCloudLiveIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Films_IndexHtml_LocalMatchesServer_UsingCommunicatorConfig()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProtoLinkCommunicator");
        Assert.True(Directory.Exists(appData), $"Missing Communicator app data: {appData}");

        var settingsPath = Path.Combine(appData, "settings.json");
        var mappingsPath = Path.Combine(appData, "cloud-sync-mappings.json");
        var metadataPath = Path.Combine(appData, "cloud-sync-metadata.json");
        Assert.True(File.Exists(settingsPath), settingsPath);
        Assert.True(File.Exists(mappingsPath), mappingsPath);
        Assert.True(File.Exists(metadataPath), metadataPath);

        var settings = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(settingsPath), IntegrationTestHttp.JsonOptions)
                       ?? throw new InvalidOperationException("settings.json empty");
        var apiBase = (Environment.GetEnvironmentVariable("PROTOLINK_API_BASE")
                       ?? settings.ApiBaseAddress
                       ?? "https://protolink.ru/").TrimEnd('/') + "/";
        Environment.SetEnvironmentVariable("PROTOLINK_API_BASE", apiBase.TrimEnd('/'));

        var mappings = JsonSerializer.Deserialize<List<CloudSyncMapping>>(await File.ReadAllTextAsync(mappingsPath), IntegrationTestHttp.JsonOptions)
                       ?? new List<CloudSyncMapping>();
        Assert.NotEmpty(mappings);

        var notesRoot = settings.NotesRootPath;
        Assert.False(string.IsNullOrWhiteSpace(notesRoot), "NotesRootPath not set in settings.json");
        var mapping = mappings.FirstOrDefault(m =>
            string.Equals(Path.GetFullPath(m.LocalPath).TrimEnd('\\'), Path.GetFullPath(notesRoot!).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(mapping);
        Log($"Mapping: {mapping!.CloudFolderName} ({mapping.CloudFolderId}) → {mapping.LocalPath}");
        Log($"API: {apiBase}");

        var relPath = (Environment.GetEnvironmentVariable("PROTOLINK_COMPARE_REL_PATH") ?? "Films/index.html")
            .Replace('\\', '/').Trim('/');
        var localFile = Path.Combine(mapping.LocalPath, relPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(localFile), $"Local file missing: {localFile}");
        var localBytes = await File.ReadAllBytesAsync(localFile);
        var localSha = Sha256Hex(localBytes);
        Log($"Local: {localFile}");
        Log($"Local size={localBytes.Length} sha256={localSha}");
        Log($"Local preview: {Preview(Encoding.UTF8.GetString(localBytes))}");

        var mappingId = mapping.CloudFolderId.ToString("N");
        var metaNorm = SyncPathUtil.Normalize(relPath);
        var metaFile = JsonSerializer.Deserialize<SyncMetadataFileDto>(await File.ReadAllTextAsync(metadataPath), IntegrationTestHttp.JsonOptions);
        var metaItems = metaFile?.Items ?? new List<SyncItemMeta>();
        var meta = metaItems.FirstOrDefault(m =>
            string.Equals(m.MappingId, mappingId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(SyncPathUtil.Normalize(m.RelativePath), metaNorm, StringComparison.Ordinal)
            && !m.IsFolder);
        Assert.NotNull(meta);
        Log($"Metadata RemoteId={meta!.RemoteId} Parent={meta.ParentRemoteId} SizeBytes={meta.SizeBytes} RemoteUpdate={meta.RemoteUpdateTime:o}");

        using var http = new HttpClient();
        IntegrationTestHttp.ConfigureClient(http);
        await AuthenticateFromCommunicatorOrEnvAsync(http, appData);

        var api = new CloudApiService(http);

        // Live folder listing under Films (detect trash / name mismatches)
        var filmsChildren = await api.GetEntitiesAsync(new[] { meta.ParentRemoteId }, includeValues: true);
        Log($"Server Films folder ({meta.ParentRemoteId}) children={filmsChildren.Count}:");
        foreach (var c in filmsChildren.OrderBy(x => CloudApiService.GetDisplayName(x), StringComparer.OrdinalIgnoreCase))
        {
            var name = CloudApiService.GetDisplayName(c);
            var synthetic = name.StartsWith("file-", StringComparison.OrdinalIgnoreCase)
                            && Guid.TryParse(name.AsSpan(5), out _);
            Log($"  [{c.Code}] {name} id={c.Id:N} ver={c.Version}{(synthetic ? " SYNTHETIC" : "")}");
        }

        await using var remoteStream = await api.GetFileStreamAsync(meta.RemoteId);
        using var ms = new MemoryStream();
        await remoteStream.CopyToAsync(ms);
        var remoteBytes = ms.ToArray();
        var remoteSha = Sha256Hex(remoteBytes);
        Log($"Remote blob id={meta.RemoteId:N} size={remoteBytes.Length} sha256={remoteSha}");
        Log($"Remote preview: {Preview(Encoding.UTF8.GetString(remoteBytes))}");

        var sizeMatch = localBytes.Length == remoteBytes.Length;
        var hashMatch = string.Equals(localSha, remoteSha, StringComparison.OrdinalIgnoreCase);
        var metaSizeMatch = meta.SizeBytes == localBytes.Length;

        Log($"RESULT sizeMatch={sizeMatch} hashMatch={hashMatch} metaSizeMatch={metaSizeMatch}");
        Log($"Synced={hashMatch}");

        Assert.True(hashMatch,
            $"Not synced. Local sha={localSha} ({localBytes.Length} bytes), remote sha={remoteSha} ({remoteBytes.Length} bytes).");
        Assert.True(metaSizeMatch,
            $"Local metadata SizeBytes={meta.SizeBytes} disagrees with local file {localBytes.Length}.");
    }

    private async Task AuthenticateFromCommunicatorOrEnvAsync(HttpClient http, string appData)
    {
        // 1) Explicit bearer
        var envToken = Environment.GetEnvironmentVariable("PROTOLINK_ACCESS_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken))
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", envToken);
            if (await ProbeAuthorizedAsync(http))
            {
                Log("Auth: PROTOLINK_ACCESS_TOKEN");
                return;
            }
            Log("Auth: PROTOLINK_ACCESS_TOKEN rejected; trying token.dat / password");
        }

        // 2) token.dat (+ refresh)
        var tokenService = new TokenService(NullLogger<TokenService>.Instance);
        var token = tokenService.LoadToken();
        if (token != null && !string.IsNullOrWhiteSpace(token.AccessToken))
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            if (await ProbeAuthorizedAsync(http))
            {
                Log($"Auth: token.dat access (login={token.Login})");
                return;
            }

            if (token.RefreshToken is Guid refresh)
            {
                Log("Auth: access expired/invalid; refreshing via token.dat");
                var refreshResp = await http.PostAsJsonAsync("api/Authentication/refreshtoken", new
                {
                    accessToken = token.AccessToken,
                    refreshToken = refresh
                });
                var refreshBody = await refreshResp.Content.ReadAsStringAsync();
                if (refreshResp.IsSuccessStatusCode
                    && !string.IsNullOrWhiteSpace(refreshBody)
                    && !string.Equals(refreshBody.Trim(), "null", StringComparison.OrdinalIgnoreCase))
                {
                    using var doc = JsonDocument.Parse(refreshBody);
                    var newAccess = doc.RootElement.GetProperty("accessToken").GetString();
                    Guid? newRefresh = null;
                    if (doc.RootElement.TryGetProperty("refreshToken", out var rt)
                        && rt.ValueKind == JsonValueKind.String
                        && Guid.TryParse(rt.GetString(), out var g))
                        newRefresh = g;
                    Assert.False(string.IsNullOrWhiteSpace(newAccess));
                    token.AccessToken = newAccess!;
                    if (newRefresh.HasValue) token.RefreshToken = newRefresh;
                    if (doc.RootElement.TryGetProperty("expirationTime", out var exp)
                        && exp.TryGetDateTime(out var expDt))
                        token.ExpirationTime = expDt;
                    tokenService.SaveToken(token);
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
                    Assert.True(await ProbeAuthorizedAsync(http), "Refreshed token still unauthorized");
                    Log("Auth: token.dat refreshed");
                    return;
                }

                Log($"Auth: refresh failed HTTP {(int)refreshResp.StatusCode}: {IntegrationTestHttp.Truncate(refreshBody, 200)}");
            }
        }

        // 3) password login
        var password = Environment.GetEnvironmentVariable("PROTOLINK_TEST_PASSWORD");
        Assert.False(string.IsNullOrWhiteSpace(password),
            "Need a valid Communicator token.dat session or PROTOLINK_TEST_PASSWORD / PROTOLINK_ACCESS_TOKEN.");
        var login = Environment.GetEnvironmentVariable("PROTOLINK_TEST_LOGIN") ?? MessengerTestCredentials.PrimaryLogin;
        var loginResp = await http.PostAsJsonAsync("api/Authentication/login", new { login, password });
        var loginBody = await IntegrationTestHttp.AssertSuccessAsync(loginResp, "login");
        using var loginDoc = JsonDocument.Parse(loginBody);
        var access = loginDoc.RootElement.TryGetProperty("accessToken", out var at)
            ? at.GetString()
            : loginDoc.RootElement.GetProperty("token").GetString();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        Log($"Auth: password login ({login})");
    }

    private static async Task<bool> ProbeAuthorizedAsync(HttpClient http)
    {
        try
        {
            var resp = await http.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
            {
                ParentIds = Array.Empty<Guid>(),
                IncludeValues = false,
                Take = 1
            });
            // 401 = bad token; other statuses still prove auth pipeline accepted the bearer
            return resp.StatusCode != System.Net.HttpStatusCode.Unauthorized;
        }
        catch
        {
            return false;
        }
    }

    private static string Sha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Preview(string text, int max = 180)
    {
        var oneLine = text.Replace("\r", "").Replace("\n", "\\n");
        return oneLine.Length <= max ? oneLine : oneLine[..max] + "…";
    }

    private void Log(string message) => _output.WriteLine(message);

    private sealed class SyncMetadataFileDto
    {
        public List<SyncItemMeta> Items { get; set; } = new();
    }
}
