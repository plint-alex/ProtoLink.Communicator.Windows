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
/// Cloud materialize before local upload (Android parity): polluted local junk must not
/// starve / abort the pass before missing remote children land. NFR-SYNC-1 soft-fail.
/// </summary>
[Trait("Category", "Offline")]
public class SyncCloudFirstMaterializeTests
{
    private static readonly Guid NameTypeId = CloudEntityCodes.NameTypeId;

    [Fact]
    public async Task Reconcile_RemoteMissingFile_MaterializesDespiteLocalJunk_WhenUploadsFail()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-cf-mat-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-cf-mat-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        Directory.CreateDirectory(Path.Combine(root, "films"));
        Directory.CreateDirectory(Path.Combine(root, "keep"));
        const string keepBody = "keep-local-body-xxxxxxxxxxxxxxxxxxxxxxxx";
        File.WriteAllText(Path.Combine(root, "keep", "index.html"), keepBody);
        for (var i = 0; i < 12; i++)
            File.WriteAllBytes(Path.Combine(root, $"evidence-{i}.png"), new byte[512]);

        var mappingId = Guid.NewGuid().ToString("N");
        var cloudFolderId = Guid.NewGuid();
        var filmsFolderId = Guid.NewGuid();
        var filmsFileId = Guid.NewGuid();
        var keepFolderId = Guid.NewGuid();
        var keepFileId = Guid.NewGuid();
        var store = new JsonSyncMetadataStore(metaDir);
        // Non-empty file meta so folders-only bootstrap heal does not run — phase order only.
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = keepFolderId,
            ParentRemoteId = cloudFolderId,
            RelativePath = "keep",
            IsFolder = true,
            SizeBytes = 0,
            RemoteUpdateTime = DateTime.UtcNow.AddHours(-1)
        });
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = keepFileId,
            ParentRemoteId = keepFolderId,
            RelativePath = "keep/index.html",
            IsFolder = false,
            SizeBytes = keepBody.Length,
            ContentHash = ContentHashUtil.Sha256Hex(Encoding.UTF8.GetBytes(keepBody)),
            RemoteUpdateTime = DateTime.UtcNow.AddHours(-1)
        });
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = filmsFolderId,
            ParentRemoteId = cloudFolderId,
            RelativePath = "films",
            IsFolder = true,
            SizeBytes = 0,
            RemoteUpdateTime = DateTime.UtcNow.AddHours(-1)
        });

        const string cloudBody = "from-cloud";
        using var http = new HttpClient(new StarveHttpHandler(
            cloudFolderId,
            filmsFolderId,
            filmsFileId,
            Encoding.UTF8.GetBytes(cloudBody),
            failAddEntity: true,
            keepFolderId: keepFolderId,
            keepFileId: keepFileId,
            keepBytes: Encoding.UTF8.GetBytes(keepBody)))
        {
            BaseAddress = new Uri("http://sync-test.local/")
        };
        var engine = new SyncEngine(store, new CloudApiService(http));
        var mapping = new SyncMappingInfo
        {
            Id = mappingId,
            CloudFolderId = cloudFolderId,
            LocalRootPath = root
        };

        var ex = await Record.ExceptionAsync(() => engine.ReconcileAllAsync(new[] { mapping }));

        // Local junk upload may still throw after materialize; cloud-first means the remote
        // child is already on disk (old local-first aborted before ApplyRemotePhase).
        Assert.NotNull(ex);
        var filmsPath = Path.Combine(root, "films", "index.html");
        Assert.True(File.Exists(filmsPath), "remote films/index.html must materialize before local upload starvation");
        Assert.Equal(cloudBody, File.ReadAllText(filmsPath));

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Reconcile_FoldersOnlyMeta_HealsBootstrapAndPullsRemoteFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-cf-boot-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-cf-boot-meta-" + Guid.NewGuid().ToString("N"));
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

        const string cloudBody = "bootstrap-heal";
        using var http = new HttpClient(new StarveHttpHandler(
            cloudFolderId,
            filmsFolderId,
            filmsFileId,
            Encoding.UTF8.GetBytes(cloudBody),
            failAddEntity: false))
        {
            BaseAddress = new Uri("http://sync-test.local/")
        };
        var engine = new SyncEngine(store, new CloudApiService(http));
        await engine.ReconcileAllAsync(new[]
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

    /// <summary>Root → keep + films; optional AddEntity failure to starve local phase.</summary>
    private sealed class StarveHttpHandler : HttpMessageHandler
    {
        private readonly Guid _rootFolderId;
        private readonly Guid _filmsFolderId;
        private readonly Guid _filmsFileId;
        private readonly byte[] _fileBytes;
        private readonly bool _failAddEntity;
        private readonly Guid? _keepFolderId;
        private readonly Guid? _keepFileId;
        private readonly byte[]? _keepBytes;

        public StarveHttpHandler(
            Guid rootFolderId,
            Guid filmsFolderId,
            Guid filmsFileId,
            byte[] fileBytes,
            bool failAddEntity,
            Guid? keepFolderId = null,
            Guid? keepFileId = null,
            byte[]? keepBytes = null)
        {
            _rootFolderId = rootFolderId;
            _filmsFolderId = filmsFolderId;
            _filmsFileId = filmsFileId;
            _fileBytes = fileBytes;
            _failAddEntity = failAddEntity;
            _keepFolderId = keepFolderId;
            _keepFileId = keepFileId;
            _keepBytes = keepBytes;
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
                if (id == _filmsFileId)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(_fileBytes)
                    });
                }
                if (_keepFileId is Guid kid && id == kid && _keepBytes != null)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(_keepBytes)
                    });
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
            if (path.Contains("AddEntity", StringComparison.OrdinalIgnoreCase))
            {
                if (_failAddEntity)
                    throw new HttpRequestException("simulated upload starvation");
                var id = Guid.NewGuid();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{{\"id\":\"{id}\"}}", Encoding.UTF8, "application/json")
                });
            }
            if (path.Contains("addFile", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                });
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
                if (_keepFolderId is Guid kf)
                    list.Add(EntityJson(kf, CloudEntityCodes.CloudFolder, "keep"));
                list.Add(EntityJson(_filmsFolderId, CloudEntityCodes.CloudFolder, "films"));
            }
            else if (_keepFolderId is Guid keepF && parentId == keepF && _keepFileId is Guid keepFile)
                list.Add(EntityJson(keepFile, CloudEntityCodes.CloudFile, "index.html"));
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
