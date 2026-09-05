using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using Xunit;
using Xunit.Abstractions;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

public class SyncRootListingLiveTests
{
    private readonly ITestOutputHelper _output;
    public SyncRootListingLiveTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task List_MyNotesFolder_TopLevel_Children()
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
        var sw = Stopwatch.StartNew();
        var children = await api.GetEntitiesAsync(new[] { root }, includeValues: true);
        sw.Stop();
        _output.WriteLine($"Top-level children={children.Count} in {sw.Elapsed}");
        foreach (var c in children.OrderBy(x => CloudApiService.GetDisplayName(x)))
        {
            var name = CloudApiService.GetDisplayName(c);
            _output.WriteLine($"  [{c.Code}] {name} id={c.Id:N} update={c.UpdateTime:o}");
        }
        Assert.NotEmpty(children);
    }
}
