using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Sync;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Win Force Download progressive semantics (Android parity): early ghost prune,
/// soft-fail null getFile continue, folders-only Push upgrades to full reconcile.
/// </summary>
[Trait("Category", "Offline")]
public class SyncForcePullProgressiveTests
{
    private static readonly Guid NameTypeId = CloudEntityCodes.NameTypeId;

    [Fact]
    public void ForcePathPriority_AcFoldersFirst()
    {
        Assert.Equal(0, SyncEngine.ForcePathPriority("films/a.mkv"));
        Assert.Equal(0, SyncEngine.ForcePathPriority("agentteamverify/x"));
        Assert.Equal(1, SyncEngine.ForcePathPriority("agents/notes.md"));
        Assert.Equal(1, SyncEngine.ForcePathPriority("mytest/file.txt"));
        Assert.Equal(2, SyncEngine.ForcePathPriority("other/file.txt"));
        Assert.Equal(2, SyncEngine.ForcePathPriority("loose-root.bin"));
    }

    [Fact]
    public async Task ForcePull_PrunesLocalGhost_AndMaterializesRemote()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-force-prune-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-force-prune-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        Directory.CreateDirectory(Path.Combine(root, "films"));
        File.WriteAllText(Path.Combine(root, "ghost-root.txt"), "local-only-ghost");
        File.WriteAllText(Path.Combine(root, "films", "ghost-film.txt"), "ghost");

        var mappingId = Guid.NewGuid().ToString("N");
        var cloudFolderId = Guid.NewGuid();
        var filmsFolderId = Guid.NewGuid();
        var filmsFileId = Guid.NewGuid();
        var store = new JsonSyncMetadataStore(metaDir);

        const string cloudBody = "from-cloud-force";
        using var http = new HttpClient(new ForceHttpHandler(
            cloudFolderId,
            filmsFolderId,
            filmsFileId,
            Encoding.UTF8.GetBytes(cloudBody),
            nullFailFileId: null))
        {
            BaseAddress = new Uri("http://sync-test.local/")
        };
        var engine = new SyncEngine(store, new CloudApiService(http));
        await engine.ForcePullMappingAsync(new SyncMappingInfo
        {
            Id = mappingId,
            CloudFolderId = cloudFolderId,
            LocalRootPath = root
        });

        Assert.False(File.Exists(Path.Combine(root, "ghost-root.txt")), "root ghost must be pruned");
        Assert.False(File.Exists(Path.Combine(root, "films", "ghost-film.txt")), "nested ghost must be pruned");
        Assert.Equal(cloudBody, File.ReadAllText(Path.Combine(root, "films", "index.html")));
        Assert.Contains(store.GetAll(mappingId), m => !m.IsFolder && m.RemoteId == filmsFileId);

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task ForcePull_NullGetFile_SoftFailsAndContinuesSibling()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-force-soft-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-force-soft-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);

        var mappingId = Guid.NewGuid().ToString("N");
        var cloudFolderId = Guid.NewGuid();
        var filmsFolderId = Guid.NewGuid();
        var goodFileId = Guid.NewGuid();
        var badFileId = Guid.NewGuid();
        var store = new JsonSyncMetadataStore(metaDir);

        const string cloudBody = "sibling-ok";
        using var http = new HttpClient(new ForceHttpHandler(
            cloudFolderId,
            filmsFolderId,
            goodFileId,
            Encoding.UTF8.GetBytes(cloudBody),
            nullFailFileId: badFileId,
            extraRootFileId: badFileId,
            extraRootFileName: "missing.bin"))
        {
            BaseAddress = new Uri("http://sync-test.local/")
        };
        var engine = new SyncEngine(store, new CloudApiService(http));
        var ex = await Record.ExceptionAsync(() => engine.ForcePullMappingAsync(new SyncMappingInfo
        {
            Id = mappingId,
            CloudFolderId = cloudFolderId,
            LocalRootPath = root
        }));

        Assert.Null(ex);
        Assert.Equal(cloudBody, File.ReadAllText(Path.Combine(root, "films", "index.html")));
        Assert.False(File.Exists(Path.Combine(root, "missing.bin")));

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task PushLocal_FoldersOnlyMeta_UpgradesToFullReconcile()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-push-folders-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-push-folders-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        Directory.CreateDirectory(Path.Combine(root, "films"));

        var mappingId = Guid.NewGuid().ToString("N");
        var cloudFolderId = Guid.NewGuid();
        var filmsFolderId = Guid.NewGuid();
        var filmsFileId = Guid.NewGuid();
        var store = new JsonSyncMetadataStore(metaDir);
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = filmsFolderId,
            ParentRemoteId = cloudFolderId,
            RelativePath = "films",
            IsFolder = true,
            SizeBytes = 0
        });

        const string cloudBody = "push-folders-heal";
        using var http = new HttpClient(new ForceHttpHandler(
            cloudFolderId,
            filmsFolderId,
            filmsFileId,
            Encoding.UTF8.GetBytes(cloudBody),
            nullFailFileId: null))
        {
            BaseAddress = new Uri("http://sync-test.local/")
        };
        var engine = new SyncEngine(store, new CloudApiService(http));
        await engine.PushLocalChangesAsync(new[]
        {
            new SyncMappingInfo
            {
                Id = mappingId,
                CloudFolderId = cloudFolderId,
                LocalRootPath = root
            }
        });

        Assert.Equal(cloudBody, File.ReadAllText(Path.Combine(root, "films", "index.html")));
        Assert.Contains(store.GetAll(mappingId), m => !m.IsFolder && m.RemoteId == filmsFileId);

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    private sealed class ForceHttpHandler : HttpMessageHandler
    {
        private readonly Guid _rootFolderId;
        private readonly Guid _filmsFolderId;
        private readonly Guid _filmsFileId;
        private readonly byte[] _fileBytes;
        private readonly Guid? _nullFailFileId;
        private readonly Guid? _extraRootFileId;
        private readonly string? _extraRootFileName;

        public ForceHttpHandler(
            Guid rootFolderId,
            Guid filmsFolderId,
            Guid filmsFileId,
            byte[] fileBytes,
            Guid? nullFailFileId,
            Guid? extraRootFileId = null,
            string? extraRootFileName = null)
        {
            _rootFolderId = rootFolderId;
            _filmsFolderId = filmsFolderId;
            _filmsFileId = filmsFileId;
            _fileBytes = fileBytes;
            _nullFailFileId = nullFailFileId;
            _extraRootFileId = extraRootFileId;
            _extraRootFileName = extraRootFileName;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("GetEntities", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(GetEntities(request));
            if (path.Contains("getFile", StringComparison.OrdinalIgnoreCase))
            {
                var idText = path.Split('/').LastOrDefault() ?? "";
                if (!Guid.TryParse(idText, out var id))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                if (_nullFailFileId is Guid bad && id == bad)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                if (id == _filmsFileId)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(_fileBytes)
                    });
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }

        private HttpResponseMessage GetEntities(HttpRequestMessage request)
        {
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "{}";
            using var doc = JsonDocument.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
            Guid? parentId = null;
            if (doc.RootElement.TryGetProperty("parentIds", out var parents)
                && parents.ValueKind == JsonValueKind.Array
                && parents.GetArrayLength() > 0)
                parentId = parents[0].GetGuid();

            var list = new List<object>();
            if (parentId == _rootFolderId)
            {
                list.Add(EntityJson(_filmsFolderId, CloudEntityCodes.CloudFolder, "films"));
                if (_extraRootFileId is Guid extra && !string.IsNullOrEmpty(_extraRootFileName))
                    list.Add(EntityJson(extra, CloudEntityCodes.CloudFile, _extraRootFileName));
            }
            else if (parentId == _filmsFolderId)
                list.Add(EntityJson(_filmsFileId, CloudEntityCodes.CloudFile, "index.html"));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(list), Encoding.UTF8, "application/json")
            };
        }

        private static object EntityJson(Guid id, string code, string name) => new
        {
            id,
            code,
            version = 1,
            creationTime = DateTime.UtcNow,
            updateTime = DateTime.UtcNow,
            values = new[]
            {
                new
                {
                    id = Guid.NewGuid(),
                    type = "String",
                    value = name,
                    parents = new[] { NameTypeId }
                }
            }
        };
    }
}
