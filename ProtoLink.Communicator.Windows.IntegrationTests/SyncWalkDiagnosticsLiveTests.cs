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

public class SyncWalkDiagnosticsLiveTests
{
    private readonly ITestOutputHelper _output;
    public SyncWalkDiagnosticsLiveTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Diagnose_WalkRemote_AndClassifyCounts()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProtoLinkCommunicator");
        var settings = JsonSerializer.Deserialize<AppSettings>(
            await File.ReadAllTextAsync(Path.Combine(appData, "settings.json")),
            IntegrationTestHttp.JsonOptions)!;
        var notesId = Guid.Parse("bcd7a62e-42a8-4cb9-95a7-7893b4c92752");
        Environment.SetEnvironmentVariable(
            "PROTOLINK_API_BASE",
            (settings.ApiBaseAddress ?? "https://protolink.ru/").TrimEnd('/'));

        using var http = new HttpClient();
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
        var store = new JsonSyncMetadataStore(appData);
        var mappingId = notesId.ToString("N");
        var meta = store.GetAll(mappingId);
        _output.WriteLine($"Store items={meta.Count} files={meta.Count(m => !m.IsFolder)}");

        var located = new List<object>();
        // Use CloudApiService walk manually
        var sw = Stopwatch.StartNew();
        var queue = new Queue<(Guid id, string path)>();
        queue.Enqueue((notesId, ""));
        var remoteFiles = 0;
        var remoteFolders = 0;
        var apiCalls = 0;
        while (queue.Count > 0)
        {
            var (id, path) = queue.Dequeue();
            apiCalls++;
            var children = await api.GetEntitiesAsync(new[] { id }, includeValues: true);
            foreach (var c in children)
            {
                var name = CloudApiService.GetDisplayName(c);
                var rel = string.IsNullOrEmpty(path) ? name : $"{path}/{name}";
                var folder = c.Code?.Contains("Folder", StringComparison.OrdinalIgnoreCase) == true;
                if (folder)
                {
                    remoteFolders++;
                    queue.Enqueue((c.Id, SyncPathUtil.Normalize(rel)));
                }
                else
                {
                    remoteFiles++;
                }
            }
        }
        sw.Stop();
        _output.WriteLine($"Walk apiCalls={apiCalls} remoteFiles={remoteFiles} remoteFolders={remoteFolders} elapsed={sw.Elapsed}");
        _ = located;

        var snap = SyncEngine.SnapshotFs(settings.NotesRootPath!);
        _output.WriteLine($"Local snap files={snap.Count(s => !s.IsFolder)} folders={snap.Count(s => s.IsFolder)}");

        var localClassifier = new LocalChangeClassifier();
        var changes = localClassifier.Classify(snap, meta);
        _output.WriteLine($"Local Added={changes.OfType<LocalChange.Added>().Count()} Updated={changes.OfType<LocalChange.Updated>().Count()} Removed={changes.OfType<LocalChange.Removed>().Count()} Renamed={changes.OfType<LocalChange.Renamed>().Count()}");
        foreach (var a in changes.OfType<LocalChange.Added>().Take(15))
            _output.WriteLine($"  ADD {a.Entry.RelativePath} size={a.Entry.SizeBytes}");
        foreach (var r in changes.OfType<LocalChange.Removed>().Take(15))
            _output.WriteLine($"  REM {r.Meta.RelativePath}");
    }
}
