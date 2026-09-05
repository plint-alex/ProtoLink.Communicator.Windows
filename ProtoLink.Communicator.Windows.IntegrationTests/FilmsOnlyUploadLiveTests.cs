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

/// <summary>
/// Live Films-only: if local≠cloud (empty ContentHash / same size), upload via SyncDirectionDecide path
/// then assert cloud matches local — without walking the whole mapped tree.
/// </summary>
public class FilmsOnlyUploadLiveTests
{
    private readonly ITestOutputHelper _output;
    public FilmsOnlyUploadLiveTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Films_EmptyHashSameSize_UploadsLocalToCloud()
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

        using var http = new HttpClient();
        IntegrationTestHttp.ConfigureClient(http);
        await AuthenticateAsync(http);

        var api = new CloudApiService(http);
        var store = new JsonSyncMetadataStore(appData);
        var mappingId = Guid.Parse("bcd7a62e-42a8-4cb9-95a7-7893b4c92752").ToString("N");
        var meta = store.GetAll(mappingId)
            .FirstOrDefault(m => !m.IsFolder && SyncPathUtil.Normalize(m.RelativePath) == "films/index.html");
        Assert.NotNull(meta);

        var localPath = Path.Combine(settings.NotesRootPath!, "Films", "index.html");
        Assert.True(File.Exists(localPath), localPath);
        var localBytes = await File.ReadAllBytesAsync(localPath);
        var localHash = ContentHashUtil.Sha256Hex(localBytes);
        var localSize = localBytes.LongLength;
        _output.WriteLine($"Local size={localSize} hash={localHash} metaHash='{meta!.ContentHash}' metaSize={meta.SizeBytes}");

        await using var remoteStream = await api.GetFileStreamOrNotFoundAsync(meta.RemoteId);
        Assert.NotNull(remoteStream);
        using var ms = new MemoryStream();
        await remoteStream!.CopyToAsync(ms);
        var remoteBytes = ms.ToArray();
        var remoteHash = ContentHashUtil.Sha256Hex(remoteBytes);
        _output.WriteLine($"Remote size={remoteBytes.Length} hash={remoteHash}");

        var action = ContentHashUtil.DecideByHash(localHash, remoteHash, meta.ContentHash);
        _output.WriteLine($"DecideByHash={action}");

        if (action == SyncDirectionDecide.Action.Write
            || !string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase))
        {
            await using var content = File.OpenRead(localPath);
            await api.AddFileAsync(meta.RemoteId, "index.html", content, "text/html");
            meta.SizeBytes = localSize;
            meta.ContentHash = localHash;
            meta.RemoteUpdateTime = DateTime.UtcNow;
            store.Upsert(meta);
            _output.WriteLine("Uploaded Films/index.html");
        }
        else if (string.IsNullOrEmpty(meta.ContentHash))
        {
            meta.ContentHash = localHash;
            store.Upsert(meta);
        }

        await using var verifyStream = await api.GetFileStreamOrNotFoundAsync(meta.RemoteId);
        Assert.NotNull(verifyStream);
        using var verifyMs = new MemoryStream();
        await verifyStream!.CopyToAsync(verifyMs);
        var cloud = System.Text.Encoding.UTF8.GetString(verifyMs.ToArray());
        var local = System.Text.Encoding.UTF8.GetString(localBytes);
        _output.WriteLine($"Cloud tail: …{Tail(cloud)}");
        Assert.Contains("Test5", cloud);
        Assert.Equal(local.TrimStart('\uFEFF'), cloud.TrimStart('\uFEFF'));
    }

    private static string Tail(string s) => s.Length <= 60 ? s : s[^60..];

    private async Task AuthenticateAsync(HttpClient http)
    {
        var tokenService = new TokenService(NullLogger<TokenService>.Instance);
        var token = tokenService.LoadToken();
        Assert.NotNull(token);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        if (await ProbeOk(http)) return;

        Assert.True(token.RefreshToken is Guid);
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
        Assert.True(await ProbeOk(http));
    }

    private static async Task<bool> ProbeOk(HttpClient http)
    {
        var resp = await http.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
        {
            ParentIds = Array.Empty<Guid>(),
            IncludeValues = false,
            Take = 1
        });
        return resp.StatusCode != System.Net.HttpStatusCode.Unauthorized;
    }
}
