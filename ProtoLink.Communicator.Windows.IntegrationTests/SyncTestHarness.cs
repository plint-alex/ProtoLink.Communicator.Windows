using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Sync;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Live API harness for SyncEngine E2E: isolated cloud folder + two local device roots.
/// Never touches production MyNotesFolder mappings.
/// </summary>
internal sealed class SyncTestHarness : IAsyncDisposable
{
    private readonly List<string> _tempDirs = new();
    private Guid? _cloudFolderId;
    private Guid? _cloudRootId;
    private bool _cleaned;

    public HttpClient Http { get; }
    public CloudApiService Api { get; }
    public Guid UserId { get; private set; }
    public Guid CloudFolderId => _cloudFolderId ?? throw new InvalidOperationException("Not initialized.");
    public string CloudFolderName { get; private set; } = "";
    public string MappingId => CloudFolderId.ToString("N");

    public string DeviceARoot { get; private set; } = "";
    public string DeviceBRoot { get; private set; } = "";
    public JsonSyncMetadataStore StoreA { get; private set; } = null!;
    public JsonSyncMetadataStore StoreB { get; private set; } = null!;
    public SyncEngine EngineA { get; private set; } = null!;
    public SyncEngine EngineB { get; private set; } = null!;

    private SyncTestHarness(HttpClient http)
    {
        Http = http;
        Api = new CloudApiService(http);
    }

    public static async Task<SyncTestHarness?> TryCreateAsync()
    {
        var password = Environment.GetEnvironmentVariable("PROTOLINK_TEST_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
            return null;

        var login = Environment.GetEnvironmentVariable("PROTOLINK_TEST_LOGIN") ?? MessengerTestCredentials.PrimaryLogin;
        var http = new HttpClient();
        IntegrationTestHttp.ConfigureClient(http);

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

        var harness = new SyncTestHarness(http) { UserId = userId };
        await harness.InitializeAsync();
        return harness;
    }

    private async Task InitializeAsync()
    {
        _cloudRootId = await EnsureCloudRootAsync();
        CloudFolderName = "sync-e2e-" + Guid.NewGuid().ToString("N")[..10];
        _cloudFolderId = await Api.AddEntityAsync(
            CloudEntityCodes.CloudFolder,
            new[] { _cloudRootId.Value },
            CloudFolderName);

        DeviceARoot = CreateTempDir("deviceA");
        DeviceBRoot = CreateTempDir("deviceB");
        var metaA = CreateTempDir("metaA");
        var metaB = CreateTempDir("metaB");

        StoreA = new JsonSyncMetadataStore(metaA);
        StoreB = new JsonSyncMetadataStore(metaB);
        EngineA = new SyncEngine(StoreA, Api);
        EngineB = new SyncEngine(StoreB, Api);
    }

    private string CreateTempDir(string label)
    {
        var path = Path.Combine(Path.GetTempPath(), "ProtoLinkSyncE2E", CloudFolderName, label + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(path);
        _tempDirs.Add(path);
        return path;
    }

    private async Task<Guid> EnsureCloudRootAsync()
    {
        var list = await Api.GetEntitiesAsync(new[] { UserId }, includeValues: false);
        var root = list.FirstOrDefault(e => e.Code == CloudEntityCodes.CloudRoot);
        if (root != null) return root.Id;
        return await Api.AddEntityAsync(CloudEntityCodes.CloudRoot, new[] { UserId });
    }

    public SyncMappingInfo MappingFor(string localRoot) => new()
    {
        Id = MappingId,
        CloudFolderId = CloudFolderId,
        LocalRootPath = Path.GetFullPath(localRoot),
        CloudFolderName = CloudFolderName
    };

    public Task ReconcileAAsync(CancellationToken ct = default) =>
        EngineA.ReconcileAllAsync(new[] { MappingFor(DeviceARoot) }, ct);

    public Task ReconcileBAsync(CancellationToken ct = default) =>
        EngineB.ReconcileAllAsync(new[] { MappingFor(DeviceBRoot) }, ct);

    public string WriteRelativeFile(string deviceRoot, string relativePath, string content)
    {
        var full = Path.Combine(deviceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return full;
    }

    public string ReadRelativeFile(string deviceRoot, string relativePath) =>
        File.ReadAllText(Path.Combine(deviceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    public bool LocalExists(string deviceRoot, string relativePath)
    {
        var full = Path.Combine(deviceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) || Directory.Exists(full);
    }

    public async Task<List<GetEntitiesResult>> ListRemoteChildrenAsync(Guid? parentId = null)
    {
        var id = parentId ?? CloudFolderId;
        return await Api.GetEntitiesAsync(new[] { id }, includeValues: true);
    }

    public async Task<List<(GetEntitiesResult Entity, string DisplayName)>> ListRemoteNamedAsync(Guid? parentId = null)
    {
        var children = await ListRemoteChildrenAsync(parentId);
        return children
            .Select(e => (e, CloudApiService.GetDisplayName(e)))
            .ToList();
    }

    public async Task<int> CountSyntheticCloudFilesAsync(Guid? parentId = null)
    {
        var named = await ListRemoteNamedAsync(parentId);
        return named.Count(x => CloudApiService.IsSyntheticCloudFileDisplayName(x.Entity, x.DisplayName));
    }

    public async Task<int> CountCloudFilesAsync(Guid? parentId = null)
    {
        var children = await ListRemoteChildrenAsync(parentId);
        return children.Count(e => e.Code == CloudEntityCodes.CloudFile);
    }

    /// <summary>Find a descendant CloudFolder/CloudFile by normalized relative path from mapping root.</summary>
    public async Task<GetEntitiesResult?> FindRemoteByRelativePathAsync(string relativePath)
    {
        var parts = SyncPathUtil.Normalize(relativePath).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var parentId = CloudFolderId;
        GetEntitiesResult? current = null;
        for (var i = 0; i < parts.Length; i++)
        {
            var children = await ListRemoteNamedAsync(parentId);
            var hit = children.FirstOrDefault(c =>
                string.Equals(SyncPathUtil.Normalize(c.DisplayName), parts[i], StringComparison.Ordinal));
            if (hit.Entity == null)
                return null;
            current = hit.Entity;
            if (i < parts.Length - 1)
            {
                if (current.Code != CloudEntityCodes.CloudFolder) return null;
                parentId = current.Id;
            }
        }
        return current;
    }

    public async Task DeleteRemoteTreeAsync(Guid entityId)
    {
        var children = await Api.GetEntitiesAsync(new[] { entityId }, includeValues: false);
        foreach (var child in children)
            await DeleteRemoteTreeAsync(child.Id);
        await Api.DeleteEntityAsync(entityId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_cleaned) return;
        _cleaned = true;
        try
        {
            if (_cloudFolderId is { } id)
                await DeleteRemoteTreeAsync(id);
        }
        catch
        {
            // Best-effort cleanup
        }

        Http.Dispose();
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }
}

internal static class SyncTestSkip
{
    public static void RequirePassword(Xunit.Abstractions.ITestOutputHelper output)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROTOLINK_TEST_PASSWORD")))
        {
            output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD");
            // Caller returns early; xUnit has no Skip.If without Xunit.SkippableFact —
            // use Assert.True(false) only when we want hard fail. Prefer soft return in tests.
        }
    }
}
