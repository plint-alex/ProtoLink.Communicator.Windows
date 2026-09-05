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

public class MyNotesFolderReconcileLiveTests
{
    private readonly ITestOutputHelper _output;
    public MyNotesFolderReconcileLiveTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task ReconcileMyNotesFolder_CompletesUnderTwoMinutes_AndSeedsHashes()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProtoLinkCommunicator");
        var settings = JsonSerializer.Deserialize<AppSettings>(
            await File.ReadAllTextAsync(Path.Combine(appData, "settings.json")),
            IntegrationTestHttp.JsonOptions)!;
        var mappings = JsonSerializer.Deserialize<List<CloudSyncMapping>>(
            await File.ReadAllTextAsync(Path.Combine(appData, "cloud-sync-mappings.json")),
            IntegrationTestHttp.JsonOptions)!;
        var notes = mappings.First(m =>
            string.Equals(Path.GetFullPath(m.LocalPath).TrimEnd('\\'),
                Path.GetFullPath(settings.NotesRootPath!).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase));

        Environment.SetEnvironmentVariable(
            "PROTOLINK_API_BASE",
            (settings.ApiBaseAddress ?? "https://protolink.ru/").TrimEnd('/'));

        using var http = new HttpClient();
        IntegrationTestHttp.ConfigureClient(http);
        await Auth(http);

        var api = new CloudApiService(http);
        var store = new JsonSyncMetadataStore(appData);
        var engine = new SyncEngine(store, api);
        var info = new SyncMappingInfo
        {
            Id = notes.CloudFolderId.ToString("N"),
            CloudFolderId = notes.CloudFolderId,
            LocalRootPath = Path.GetFullPath(notes.LocalPath),
            CloudFolderName = notes.CloudFolderName
        };

        var sw = Stopwatch.StartNew();
        await engine.ReconcileAllAsync(new[] { info }, CancellationToken.None);
        sw.Stop();
        _output.WriteLine($"Reconcile took {sw.Elapsed}");

        Assert.True(sw.Elapsed < TimeSpan.FromMinutes(2), $"Too slow: {sw.Elapsed}");
        var empty = store.GetAll(info.Id).Count(m => !m.IsFolder && string.IsNullOrEmpty(m.ContentHash));
        _output.WriteLine($"emptyHash after={empty}");
        Assert.Equal(0, empty);

        var films = store.GetAll(info.Id)
            .First(m => SyncPathUtil.Normalize(m.RelativePath) == "films/index.html");
        Assert.False(string.IsNullOrEmpty(films.ContentHash));
    }

    private async Task Auth(HttpClient http)
    {
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
        if (probe.StatusCode != System.Net.HttpStatusCode.Unauthorized) return;
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
}
