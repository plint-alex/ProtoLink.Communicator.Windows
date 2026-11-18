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
        Assert.Equal(0, SyncEngine.ForcePathPriority("Films/a.mkv"));
        Assert.Equal(0, SyncEngine.ForcePathPriority("agentteamverify/x"));
        Assert.Equal(0, SyncEngine.ForcePathPriority("nested/Films/clip.mkv"));
        Assert.Equal(1, SyncEngine.ForcePathPriority("agents/notes.md"));
        Assert.Equal(1, SyncEngine.ForcePathPriority("protolink.ru/agents/todo.md"));
        Assert.Equal(1, SyncEngine.ForcePathPriority("mytest/file.txt"));
        Assert.Equal(2, SyncEngine.ForcePathPriority("other/file.txt"));
        Assert.Equal(2, SyncEngine.ForcePathPriority("loose-root.bin"));
    }

    [Fact]
    public void ForceShouldDownloadFile_AcAndNotesIndexOnly()
    {
        Assert.True(SyncEngine.ForceShouldDownloadFile("films/clip.mkv"));
        Assert.True(SyncEngine.ForceShouldDownloadFile("Notes/index.html"));
        Assert.True(SyncEngine.ForceShouldDownloadFile("other/index.htm"));
        Assert.False(SyncEngine.ForceShouldDownloadFile("00-home.png"));
        Assert.False(SyncEngine.ForceShouldDownloadFile("evidence/dump.bin"));
        Assert.False(SyncEngine.ForceShouldDownloadFile("protolink.ru/agents/note.md"));
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

    /// <summary>
    /// Live FAIL FORCE_STALLED_NO_PRUNE_FILL: early prune must clear root ghost files before
    /// spending wall time on huge local-only directory trees (recursive delete).
    /// </summary>
    [Fact]
    public async Task ForcePull_EarlyPrune_DeletesGhostFileBeforeHugeLocalOnlyDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-force-files-first-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-force-files-first-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        // Name sorts before "ghost-root.txt" so filesystem order would delete the dir first
        // without files-first ordering — stall class on MyNotesFolder local-only trees.
        var huge = Path.Combine(root, "aaa-huge-local-only");
        Directory.CreateDirectory(huge);
        for (var i = 0; i < 80; i++)
        {
            var sub = Path.Combine(huge, "d" + i);
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "blob.bin"), new string('x', 4096));
        }

        File.WriteAllText(Path.Combine(root, "ghost-root.txt"), "planted-ghost");

        var mappingId = Guid.NewGuid().ToString("N");
        var cloudFolderId = Guid.NewGuid();
        var filmsFolderId = Guid.NewGuid();
        var filmsFileId = Guid.NewGuid();
        var store = new JsonSyncMetadataStore(metaDir);

        const string cloudBody = "films-after-file-prune";
        using var http = new HttpClient(new ForceHttpHandler(
            cloudFolderId,
            filmsFolderId,
            filmsFileId,
            Encoding.UTF8.GetBytes(cloudBody),
            nullFailFileId: null,
            filmsFolderDisplayName: "Films"))
        {
            BaseAddress = new Uri("http://sync-test.local/")
        };
        var engine = new SyncEngine(store, new CloudApiService(http));

        var pull = engine.ForcePullMappingAsync(new SyncMappingInfo
        {
            Id = mappingId,
            CloudFolderId = cloudFolderId,
            LocalRootPath = root
        });

        // Ghost file must clear promptly — not wait on recursive huge-dir delete.
        var ghostGone = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (!File.Exists(Path.Combine(root, "ghost-root.txt")))
            {
                ghostGone = true;
                break;
            }

            await Task.Delay(20);
        }

        Assert.True(ghostGone, "root ghost file must prune before huge local-only dir finishes deleting");
        await pull.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(cloudBody, File.ReadAllText(Path.Combine(root, "Films", "index.html")));
        Assert.False(Directory.Exists(huge), "local-only dir must still be pruned by Force mirror");

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>
    /// Live 898c1453 stall: wide root loose blobs must not getFile — AC Films fill + complete.
    /// </summary>
    [Fact]
    public async Task ForcePull_SkipsNonAcBlobGetFile_StillCompletesAndPrunes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-force-skip-blob-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-force-skip-blob-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        File.WriteAllText(Path.Combine(root, "ghost-root.txt"), "ghost");

        var mappingId = Guid.NewGuid().ToString("N");
        var cloudFolderId = Guid.NewGuid();
        var filmsFolderId = Guid.NewGuid();
        var filmsFileId = Guid.NewGuid();
        var blobId = Guid.NewGuid();
        var store = new JsonSyncMetadataStore(metaDir);

        const string cloudBody = "ac-fill-no-blob";
        var handler = new ForceHttpHandler(
            cloudFolderId,
            filmsFolderId,
            filmsFileId,
            Encoding.UTF8.GetBytes(cloudBody),
            nullFailFileId: null,
            extraRootFileId: blobId,
            extraRootFileName: "00-home.png",
            hangGetFileIds: new HashSet<Guid> { blobId },
            filmsFolderDisplayName: "Films");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://sync-test.local/") };
        var engine = new SyncEngine(store, new CloudApiService(http));

        await engine.ForcePullMappingAsync(new SyncMappingInfo
        {
            Id = mappingId,
            CloudFolderId = cloudFolderId,
            LocalRootPath = root
        }).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(0, handler.GetFileHits(blobId));
        Assert.False(File.Exists(Path.Combine(root, "ghost-root.txt")));
        Assert.False(File.Exists(Path.Combine(root, "00-home.png")));
        Assert.Equal(cloudBody, File.ReadAllText(Path.Combine(root, "Films", "index.html")));
        Assert.NotNull(store.GetAll(mappingId).FirstOrDefault(m => m.RemoteId == filmsFileId));

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
        private readonly string _filmsFolderDisplayName;
        private readonly HashSet<Guid> _hangGetFileIds;
        private readonly Dictionary<Guid, int> _getFileHits = new();

        public ForceHttpHandler(
            Guid rootFolderId,
            Guid filmsFolderId,
            Guid filmsFileId,
            byte[] fileBytes,
            Guid? nullFailFileId,
            Guid? extraRootFileId = null,
            string? extraRootFileName = null,
            HashSet<Guid>? hangGetFileIds = null,
            string filmsFolderDisplayName = "films")
        {
            _rootFolderId = rootFolderId;
            _filmsFolderId = filmsFolderId;
            _filmsFileId = filmsFileId;
            _fileBytes = fileBytes;
            _nullFailFileId = nullFailFileId;
            _extraRootFileId = extraRootFileId;
            _extraRootFileName = extraRootFileName;
            _hangGetFileIds = hangGetFileIds ?? new HashSet<Guid>();
            _filmsFolderDisplayName = filmsFolderDisplayName;
        }

        public int GetFileHits(Guid id)
        {
            lock (_getFileHits)
                return _getFileHits.TryGetValue(id, out var n) ? n : 0;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("GetEntities", StringComparison.OrdinalIgnoreCase))
                return GetEntities(request);
            if (path.Contains("getFile", StringComparison.OrdinalIgnoreCase))
            {
                var idText = path.Split('/').LastOrDefault() ?? "";
                if (!Guid.TryParse(idText, out var id))
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                lock (_getFileHits)
                {
                    _getFileHits.TryGetValue(id, out var n);
                    _getFileHits[id] = n + 1;
                }

                if (_hangGetFileIds.Contains(id))
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (_nullFailFileId is Guid bad && id == bad)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                if (id == _filmsFileId)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(_fileBytes)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
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
                list.Add(EntityJson(_filmsFolderId, CloudEntityCodes.CloudFolder, _filmsFolderDisplayName));
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
