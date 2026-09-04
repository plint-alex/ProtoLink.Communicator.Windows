using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using Xunit;
using Xunit.Abstractions;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// One-off cleanup: deletes CloudFile entities whose display name is the synthetic
/// <c>file-{guid}</c> fallback (nameless after AddFile version bump).
///
/// Safety: set <c>PROTOLINK_CLEANUP_TRASH=1</c> (and <c>PROTOLINK_TEST_PASSWORD</c>) to run.
/// Optional: <c>PROTOLINK_CLEANUP_FOLDER_NAME=MyNotesFolder</c> to limit to that CloudFolder under CloudRoot
/// (case-insensitive). Default walks the entire cloud tree.
///
/// <code>
/// $env:PROTOLINK_CLEANUP_TRASH = "1"
/// $env:PROTOLINK_CLEANUP_FOLDER_NAME = "MyNotesFolder"   # optional
/// $env:PROTOLINK_TEST_PASSWORD = "..."
/// dotnet test --filter "FullyQualifiedName~CloudSyntheticTrashCleanup"
/// </code>
/// </summary>
public class CloudSyntheticTrashCleanupIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public CloudSyntheticTrashCleanupIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task DeleteSyntheticFileGuidCloudFiles_UnderCloudOrNamedFolder()
    {
        if (!IsCleanupEnabled())
        {
            _output.WriteLine("Skip: set PROTOLINK_CLEANUP_TRASH=1 to run this destructive cleanup.");
            return;
        }

        var password = Environment.GetEnvironmentVariable("PROTOLINK_TEST_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            _output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD");
            return;
        }

        var login = Environment.GetEnvironmentVariable("PROTOLINK_TEST_LOGIN") ?? MessengerTestCredentials.PrimaryLogin;
        var folderFilter = Environment.GetEnvironmentVariable("PROTOLINK_CLEANUP_FOLDER_NAME")?.Trim();

        using var http = new HttpClient();
        IntegrationTestHttp.ConfigureClient(http);
        // Large trees: many GetEntities round-trips
        http.Timeout = TimeSpan.FromMinutes(30);

        var loginResp = await http.PostAsJsonAsync("api/Authentication/login", new { login, password });
        var loginBody = await IntegrationTestHttp.AssertSuccessAsync(loginResp, "login");
        using var loginDoc = JsonDocument.Parse(loginBody);
        var token = loginDoc.RootElement.TryGetProperty("accessToken", out var at)
            ? at.GetString()
            : loginDoc.RootElement.GetProperty("token").GetString();
        var userId = loginDoc.RootElement.TryGetProperty("userId", out var uid)
            ? uid.GetGuid()
            : loginDoc.RootElement.GetProperty("id").GetGuid();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var api = new CloudApiService(http);
        var roots = await api.GetEntitiesAsync(new[] { userId }, includeValues: false);
        var cloudRoot = roots.FirstOrDefault(e => e.Code == CloudEntityCodes.CloudRoot);
        Assert.NotNull(cloudRoot);

        Guid walkRoot = cloudRoot!.Id;
        var pathPrefix = "Cloud";
        if (!string.IsNullOrWhiteSpace(folderFilter))
        {
            var segments = folderFilter.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var cur = cloudRoot.Id;
            var built = "Cloud";
            foreach (var seg in segments)
            {
                var under = await api.GetEntitiesAsync(new[] { cur }, includeValues: true);
                var match = under.FirstOrDefault(e =>
                    e.Code == CloudEntityCodes.CloudFolder
                    && string.Equals(CloudApiService.GetDisplayName(e), seg, StringComparison.OrdinalIgnoreCase));
                Assert.True(match != null,
                    $"CloudFolder '{seg}' not found under {built}. " +
                    $"Children: {string.Join(", ", under.Where(c => c.Code == CloudEntityCodes.CloudFolder).Select(CloudApiService.GetDisplayName))}");
                cur = match!.Id;
                built += "/" + seg;
            }
            walkRoot = cur;
            pathPrefix = built;
            Log($"Scoped cleanup to {pathPrefix} ({walkRoot})");
        }
        else
        {
            Log($"Cleanup entire cloud under CloudRoot ({walkRoot})");
        }

        var trash = new List<(Guid Id, string Path)>();
        var foldersVisited = 0;
        await CollectSyntheticAsync(api, walkRoot, pathPrefix, trash, () =>
        {
            foldersVisited++;
            if (foldersVisited % 25 == 0)
                Log($"… visited {foldersVisited} folders, trash so far={trash.Count}");
        });

        Log($"Scan done. Folders={foldersVisited}. Found {trash.Count} synthetic CloudFile(s):");
        foreach (var t in trash.OrderBy(x => x.Path))
            Log($"  DELETE {t.Path}  ({t.Id:N})");

        var deleted = 0;
        var failed = new List<string>();
        foreach (var t in trash)
        {
            try
            {
                await api.DeleteEntityAsync(t.Id);
                deleted++;
                if (deleted % 50 == 0)
                    Log($"… deleted {deleted}/{trash.Count}");
            }
            catch (Exception ex)
            {
                failed.Add($"{t.Path}: {ex.Message}");
                Log($"  FAIL {t.Path}: {ex.Message}");
            }
        }

        var remaining = new List<(Guid Id, string Path)>();
        await CollectSyntheticAsync(api, walkRoot, pathPrefix, remaining, null);

        Log($"Deleted {deleted}/{trash.Count}. Remaining synthetic: {remaining.Count}");
        foreach (var r in remaining)
            Log($"  STILL THERE {r.Path} ({r.Id:N})");

        Assert.Empty(failed);
        Assert.Empty(remaining);
    }

    private void Log(string message)
    {
        _output.WriteLine(message);
        Console.WriteLine(message);
    }

    private static bool IsCleanupEnabled()
    {
        var v = Environment.GetEnvironmentVariable("PROTOLINK_CLEANUP_TRASH");
        return string.Equals(v, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task CollectSyntheticAsync(
        CloudApiService api,
        Guid parentId,
        string path,
        List<(Guid Id, string Path)> sink,
        Action? onFolderVisited)
    {
        // Values needed: only CloudFiles without NAME_TYPE look like file-{guid}.
        var children = await api.GetEntitiesAsync(new[] { parentId }, includeValues: true);
        onFolderVisited?.Invoke();

        var folders = new List<(Guid Id, string Path)>();
        foreach (var child in children)
        {
            var name = CloudApiService.GetDisplayName(child);
            var childPath = $"{path}/{name}";
            if (child.Code == CloudEntityCodes.CloudFile)
            {
                if (CloudApiService.IsSyntheticCloudFileDisplayName(child, name))
                    sink.Add((child.Id, childPath));
            }
            else if (child.Code == CloudEntityCodes.CloudFolder)
            {
                folders.Add((child.Id, childPath));
            }
        }

        // Depth-first; could parallelize later if API allows.
        foreach (var folder in folders)
            await CollectSyntheticAsync(api, folder.Id, folder.Path, sink, onFolderVisited);
    }
}
