using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using Xunit;
using Xunit.Abstractions;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Deletes synthetic file-{{guid}} CloudFiles under mapped MyNotesFolder using token.dat.
/// Set PROTOLINK_CLEANUP_TRASH=1 to run (destructive).
/// </summary>
public class CleanupMyNotesTrashLiveTests
{
    private readonly ITestOutputHelper _output;
    public CleanupMyNotesTrashLiveTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task DeleteSyntheticUnderMyNotesFolder_UsingTokenDat()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PROTOLINK_CLEANUP_TRASH"), "1", StringComparison.OrdinalIgnoreCase))
        {
            _output.WriteLine("Skip: set PROTOLINK_CLEANUP_TRASH=1");
            return;
        }

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProtoLinkCommunicator");
        var settings = JsonSerializer.Deserialize<AppSettings>(
            await File.ReadAllTextAsync(Path.Combine(appData, "settings.json")),
            IntegrationTestHttp.JsonOptions)!;
        Environment.SetEnvironmentVariable(
            "PROTOLINK_API_BASE",
            (settings.ApiBaseAddress ?? "https://protolink.ru/").TrimEnd('/'));

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        IntegrationTestHttp.ConfigureClient(http);
        var tokenService = new TokenService(NullLogger<TokenService>.Instance);
        var token = tokenService.LoadToken();
        Assert.NotNull(token);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        var probe = await http.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
        {
            ParentIds = Array.Empty<Guid>(),
            IncludeValues = false,
            Take = 1
        });
        if (probe.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            var refreshResp = await http.PostAsJsonAsync("api/Authentication/refreshtoken", new
            {
                accessToken = token.AccessToken,
                refreshToken = token.RefreshToken
            });
            var body = await refreshResp.Content.ReadAsStringAsync();
            Assert.True(refreshResp.IsSuccessStatusCode, body);
            using var doc = JsonDocument.Parse(body);
            token.AccessToken = doc.RootElement.GetProperty("accessToken").GetString()!;
            tokenService.SaveToken(token);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        }

        var api = new CloudApiService(http);
        var root = Guid.Parse("bcd7a62e-42a8-4cb9-95a7-7893b4c92752");
        var trash = new System.Collections.Concurrent.ConcurrentBag<(Guid Id, string Path)>();
        await CollectAsync(api, root, "MyNotesFolder", trash);
        _output.WriteLine($"Found {trash.Count} synthetic files");
        var deleted = 0;
        var failed = 0;
        var refreshGate = new SemaphoreSlim(1, 1);
        using var deleteSem = new SemaphoreSlim(8);
        var tasks = trash.Select(async t =>
        {
            await deleteSem.WaitAsync();
            try
            {
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        await api.DeleteEntityAsync(t.Id);
                        Interlocked.Increment(ref deleted);
                        return;
                    }
                    catch (CloudAuthRequiredException)
                    {
                        await refreshGate.WaitAsync();
                        try { await RefreshAuth(http, tokenService, token); }
                        finally { refreshGate.Release(); }
                        http.DefaultRequestHeaders.Authorization =
                            new AuthenticationHeaderValue("Bearer", token.AccessToken);
                    }
                    catch
                    {
                        break;
                    }
                }
                Interlocked.Increment(ref failed);
            }
            finally
            {
                deleteSem.Release();
            }
        });
        await Task.WhenAll(tasks);
        _output.WriteLine($"Deleted {deleted}, failed={failed}");
        Assert.True(deleted > 1000, $"Expected bulk delete, got {deleted}");
    }

    private static async Task RefreshAuth(HttpClient http, TokenService tokenService, TokenData token)
    {
        var refreshResp = await http.PostAsJsonAsync("api/Authentication/refreshtoken", new
        {
            accessToken = token.AccessToken,
            refreshToken = token.RefreshToken
        });
        var body = await refreshResp.Content.ReadAsStringAsync();
        refreshResp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(body);
        token.AccessToken = doc.RootElement.GetProperty("accessToken").GetString()!;
        if (doc.RootElement.TryGetProperty("refreshToken", out var rt)
            && rt.ValueKind == JsonValueKind.String
            && Guid.TryParse(rt.GetString(), out var g))
            token.RefreshToken = g;
        try { tokenService.SaveToken(token); } catch { /* app may lock token.dat */ }
    }

    private static async Task CollectAsync(
        CloudApiService api,
        Guid parentId,
        string path,
        System.Collections.Concurrent.ConcurrentBag<(Guid Id, string Path)> sink)
    {
        var children = await api.GetEntitiesAsync(new[] { parentId }, includeValues: true);
        var folders = new List<(Guid Id, string Path)>();
        foreach (var child in children)
        {
            var name = CloudApiService.GetDisplayName(child);
            var childPath = $"{path}/{name}";
            if (child.Code == CloudEntityCodes.CloudFile
                && CloudApiService.IsSyntheticCloudFileDisplayName(child, name))
                sink.Add((child.Id, childPath));
            else if (child.Code == CloudEntityCodes.CloudFolder)
                folders.Add((child.Id, childPath));
        }
        using var sem = new SemaphoreSlim(6);
        await Task.WhenAll(folders.Select(async f =>
        {
            await sem.WaitAsync();
            try { await CollectAsync(api, f.Id, f.Path, sink); }
            finally { sem.Release(); }
        }));
    }
}
