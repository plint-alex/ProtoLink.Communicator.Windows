using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Sync;
using Xunit;
using Xunit.Abstractions;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

public class SyncFolderTimingLiveTests
{
    private readonly ITestOutputHelper _output;
    public SyncFolderTimingLiveTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Time_Each_TopLevel_Child_Listing()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProtoLinkCommunicator");
        var settings = JsonSerializer.Deserialize<AppSettings>(
            await File.ReadAllTextAsync(Path.Combine(appData, "settings.json")),
            IntegrationTestHttp.JsonOptions)!;
        Environment.SetEnvironmentVariable(
            "PROTOLINK_API_BASE",
            (settings.ApiBaseAddress ?? "https://protolink.ru/").TrimEnd('/'));

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        IntegrationTestHttp.ConfigureClient(http);
        var tokenService = new TokenService(NullLogger<TokenService>.Instance);
        var token = tokenService.LoadToken()!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
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
            using var doc = JsonDocument.Parse(body);
            token.AccessToken = doc.RootElement.GetProperty("accessToken").GetString()!;
            tokenService.SaveToken(token);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        }

        var api = new CloudApiService(http);
        var root = Guid.Parse("bcd7a62e-42a8-4cb9-95a7-7893b4c92752");
        var top = await api.GetEntitiesAsync(new[] { root }, includeValues: true);
        var logPath = Path.Combine(Path.GetTempPath(), "protolink-folder-timing.txt");
        await File.WriteAllTextAsync(logPath, "");
        foreach (var c in top.OrderBy(x => CloudApiService.GetDisplayName(x)))
        {
            var name = CloudApiService.GetDisplayName(c);
            if (c.Code?.Contains("Folder", StringComparison.OrdinalIgnoreCase) != true)
            {
                var line0 = $"skip-file {name}";
                _output.WriteLine(line0);
                await File.AppendAllTextAsync(logPath, line0 + Environment.NewLine);
                continue;
            }
            var sw = Stopwatch.StartNew();
            try
            {
                var kids = await api.GetEntitiesAsync(new[] { c.Id }, includeValues: true);
                sw.Stop();
                var line = $"{sw.Elapsed.TotalSeconds:F1}s kids={kids.Count} {name}";
                _output.WriteLine(line);
                await File.AppendAllTextAsync(logPath, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var line = $"FAIL {sw.Elapsed.TotalSeconds:F1}s {name}: {ex.GetType().Name} {ex.Message}";
                _output.WriteLine(line);
                await File.AppendAllTextAsync(logPath, line + Environment.NewLine);
            }
        }
        _output.WriteLine($"log={logPath}");
        Assert.True(File.Exists(logPath));
    }

    [Fact]
    public async Task Reconcile_OnlyFilmsMapping_Fast()
    {
        // Temporary mapping: only Films folder as cloud root → local Films dir
        var appData = Path.Combine(Path.GetTempPath(), "ProtoLinkFilmsSyncTest");
        if (Directory.Exists(appData)) Directory.Delete(appData, true);
        Directory.CreateDirectory(appData);

        var settings = JsonSerializer.Deserialize<AppSettings>(
            await File.ReadAllTextAsync(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ProtoLinkCommunicator", "settings.json")),
            IntegrationTestHttp.JsonOptions)!;
        Environment.SetEnvironmentVariable(
            "PROTOLINK_API_BASE",
            (settings.ApiBaseAddress ?? "https://protolink.ru/").TrimEnd('/'));

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        IntegrationTestHttp.ConfigureClient(http);
        var tokenService = new TokenService(NullLogger<TokenService>.Instance);
        var token = tokenService.LoadToken()!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var api = new CloudApiService(http);
        var store = new JsonSyncMetadataStore(appData);
        var filmsCloud = Guid.Parse("c12f0f85-1c45-435d-be59-aa35949ba5e5");
        var localFilms = Path.Combine(settings.NotesRootPath!, "Films");
        var info = new SyncMappingInfo
        {
            Id = filmsCloud.ToString("N"),
            CloudFolderId = filmsCloud,
            LocalRootPath = localFilms,
            CloudFolderName = "Films"
        };

        // Seed meta from a one-file tree via first reconcile
        var sw = Stopwatch.StartNew();
        await new SyncEngine(store, api).ReconcileAllAsync(new[] { info }, CancellationToken.None);
        sw.Stop();
        _output.WriteLine($"Films-only reconcile {sw.Elapsed}");

        var local = await File.ReadAllTextAsync(Path.Combine(localFilms, "index.html"));
        await using var stream = await api.GetFileStreamOrNotFoundAsync(
            Guid.Parse("bc68252d-d15d-467c-abc0-cf258824721e"));
        Assert.NotNull(stream);
        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms);
        var cloud = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        _output.WriteLine($"local has Test5={local.Contains("Test5")} cloud has Test5={cloud.Contains("Test5")}");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(45), $"Slow: {sw.Elapsed}");
        Assert.Equal(local.TrimStart('\uFEFF'), cloud.TrimStart('\uFEFF'));
    }
}
