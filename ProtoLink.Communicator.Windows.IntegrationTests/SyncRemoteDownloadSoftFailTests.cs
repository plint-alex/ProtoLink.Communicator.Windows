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
/// Failed remote download (null) soft-fails per item; empty remote bytes are legitimate content.
/// </summary>
[Trait("Category", "Offline")]
public class SyncRemoteDownloadSoftFailTests
{
    private static readonly Guid NameTypeId = CloudEntityCodes.NameTypeId;

    [Fact]
    public async Task Reconcile_ComparePhase_NullDownload_SoftFailsAndContinuesSibling()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-rdl-cmp-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-rdl-cmp-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        var keepDir = Path.Combine(root, "keep");
        var failDir = Path.Combine(root, "fail");
        Directory.CreateDirectory(keepDir);
        Directory.CreateDirectory(failDir);
        const string keepBody = "keep-local-v2-body";
        const string failBody = "fail-local-body-xx";
        const string keepRemoteBody = "keep-remote-old";
        File.WriteAllText(Path.Combine(keepDir, "index.html"), keepBody);
        File.WriteAllText(Path.Combine(failDir, "index.html"), failBody);

        var mappingId = Guid.NewGuid().ToString("N");
        var cloudFolderId = Guid.NewGuid();
        var keepRemote = Guid.NewGuid();
        var failRemote = Guid.NewGuid();
        var keepFolderId = Guid.NewGuid();
        var failFolderId = Guid.NewGuid();
        var store = new JsonSyncMetadataStore(metaDir);
        // Meta hash = remote so DecideByHash → Write (upload local).
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = keepRemote,
            ParentRemoteId = keepFolderId,
            RelativePath = "keep/index.html",
            IsFolder = false,
            SizeBytes = 1,
            ContentHash = ContentHashUtil.Sha256Hex(Encoding.UTF8.GetBytes(keepRemoteBody)),
            RemoteUpdateTime = DateTime.UtcNow.AddHours(-1)
        });
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = failRemote,
            ParentRemoteId = failFolderId,
            RelativePath = "fail/index.html",
            IsFolder = false,
            SizeBytes = 1,
            ContentHash = "bb",
            RemoteUpdateTime = DateTime.UtcNow.AddHours(-1)
        });

        var uploads = new List<Guid>();
        using var http = new HttpClient(new SyncHttpHandler(
            cloudFolderId,
            new Dictionary<Guid, byte[]?>
            {
                [keepRemote] = Encoding.UTF8.GetBytes(keepRemoteBody),
                [failRemote] = null
            },
            new Dictionary<Guid, string>
            {
                [keepRemote] = "index.html",
                [failRemote] = "index.html"
            },
            folderChildren: new Dictionary<Guid, List<Guid>>(),
            fileParents: new Dictionary<Guid, Guid>(),
            uploads,
            nestedFolders: new Dictionary<string, (Guid FolderId, Guid FileId)>
            {
                ["keep"] = (keepFolderId, keepRemote),
                ["fail"] = (failFolderId, failRemote)
            }))
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

        await engine.ReconcileAllAsync(new[] { mapping });

        Assert.Contains(keepRemote, uploads);
        Assert.DoesNotContain(failRemote, uploads);
        var failMeta = store.GetAll(mappingId).Single(m => m.RemoteId == failRemote);
        Assert.Equal("bb", failMeta.ContentHash);
        var keepMeta = store.GetAll(mappingId).Single(m => m.RemoteId == keepRemote);
        Assert.Equal(ContentHashUtil.Sha256Hex(Encoding.UTF8.GetBytes(keepBody)), keepMeta.ContentHash);

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Reconcile_AdoptLocalAdd_NullDownload_SoftFailsAndContinuesSibling()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-rdl-adp-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-rdl-adp-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        var keepDir = Path.Combine(root, "keep");
        var adoptDir = Path.Combine(root, "adopt");
        Directory.CreateDirectory(keepDir);
        Directory.CreateDirectory(adoptDir);
        const string keepBody = "keep-sibling-upload-body";
        const string keepRemoteBody = "keep-remote-old";
        // Long enough that localLooksStub is false when remote size is unknown.
        const string adoptBody = "adopt-local-add-body-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";
        File.WriteAllText(Path.Combine(keepDir, "index.html"), keepBody);
        File.WriteAllText(Path.Combine(adoptDir, "index.html"), adoptBody);

        var mappingId = Guid.NewGuid().ToString("N");
        var cloudFolderId = Guid.NewGuid();
        var keepRemote = Guid.NewGuid();
        var adoptRemote = Guid.NewGuid();
        var keepFolderId = Guid.NewGuid();
        var adoptFolderId = Guid.NewGuid();
        var store = new JsonSyncMetadataStore(metaDir);
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = keepRemote,
            ParentRemoteId = keepFolderId,
            RelativePath = "keep/index.html",
            IsFolder = false,
            SizeBytes = 1,
            ContentHash = ContentHashUtil.Sha256Hex(Encoding.UTF8.GetBytes(keepRemoteBody)),
            RemoteUpdateTime = DateTime.UtcNow.AddHours(-1)
        });
        // adopt path: local Added, no meta — remote walk finds same relative path.

        var uploads = new List<Guid>();
        using var http = new HttpClient(new SyncHttpHandler(
            cloudFolderId,
            new Dictionary<Guid, byte[]?>
            {
                [keepRemote] = Encoding.UTF8.GetBytes(keepRemoteBody),
                [adoptRemote] = null
            },
            new Dictionary<Guid, string>
            {
                [keepRemote] = "index.html",
                [adoptRemote] = "index.html"
            },
            folderChildren: new Dictionary<Guid, List<Guid>>(),
            fileParents: new Dictionary<Guid, Guid>(),
            uploads,
            nestedFolders: new Dictionary<string, (Guid FolderId, Guid FileId)>
            {
                ["keep"] = (keepFolderId, keepRemote),
                ["adopt"] = (adoptFolderId, adoptRemote)
            }))
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

        await engine.ReconcileAllAsync(new[] { mapping });

        Assert.Contains(keepRemote, uploads);
        Assert.DoesNotContain(adoptRemote, uploads);
        // Soft-fail skipped adopt in local phase (no throw); sibling keep still uploaded.

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Reconcile_EmptyRemoteBytes_DoesNotThrowDownloadFailed()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-rdl-empty-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-rdl-empty-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        var fileDir = Path.Combine(root, "note");
        Directory.CreateDirectory(fileDir);
        // Non-stub local (len >= 64) so compare reaches hash decide with empty remote bytes.
        var localBody = new string('x', 64);
        File.WriteAllText(Path.Combine(fileDir, "index.html"), localBody);

        var mappingId = Guid.NewGuid().ToString("N");
        var cloudFolderId = Guid.NewGuid();
        var remoteId = Guid.NewGuid();
        var store = new JsonSyncMetadataStore(metaDir);
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = remoteId,
            ParentRemoteId = cloudFolderId,
            RelativePath = "note/index.html",
            IsFolder = false,
            SizeBytes = 1,
            ContentHash = "aa",
            RemoteUpdateTime = DateTime.UtcNow.AddHours(-1)
        });

        var noteFolderId = Guid.NewGuid();
        store.Delete(mappingId, "note/index.html");
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = remoteId,
            ParentRemoteId = noteFolderId,
            RelativePath = "note/index.html",
            IsFolder = false,
            SizeBytes = 1,
            ContentHash = "aa",
            RemoteUpdateTime = DateTime.UtcNow.AddHours(-1)
        });

        using var http = new HttpClient(new SyncHttpHandler(
            cloudFolderId,
            new Dictionary<Guid, byte[]?> { [remoteId] = Array.Empty<byte>() },
            new Dictionary<Guid, string> { [remoteId] = "index.html" },
            folderChildren: new Dictionary<Guid, List<Guid>>(),
            fileParents: new Dictionary<Guid, Guid>(),
            uploads: new List<Guid>(),
            nestedFolders: new Dictionary<string, (Guid FolderId, Guid FileId)>
            {
                ["note"] = (noteFolderId, remoteId)
            }))
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

        Assert.NotNull(ex);
        Assert.IsType<SyncConflictException>(ex);
        Assert.DoesNotContain("returned empty", ex!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("download failed", ex.Message, StringComparison.OrdinalIgnoreCase);

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Reconcile_EmptyRemoteMatchingEmptyLocal_CompletesWithoutThrow()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-rdl-empty-ok-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-rdl-empty-ok-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        var fileDir = Path.Combine(root, "note");
        Directory.CreateDirectory(fileDir);
        File.WriteAllBytes(Path.Combine(fileDir, "index.html"), Array.Empty<byte>());

        var mappingId = Guid.NewGuid().ToString("N");
        var cloudFolderId = Guid.NewGuid();
        var remoteId = Guid.NewGuid();
        var emptyHash = ContentHashUtil.Sha256Hex(Array.Empty<byte>());
        var store = new JsonSyncMetadataStore(metaDir);
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = remoteId,
            ParentRemoteId = cloudFolderId,
            RelativePath = "note/index.html",
            IsFolder = false,
            SizeBytes = 0,
            ContentHash = emptyHash,
            RemoteUpdateTime = DateTime.UtcNow.AddHours(-1)
        });

        var noteFolderId = Guid.NewGuid();
        store.Delete(mappingId, "note/index.html");
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = remoteId,
            ParentRemoteId = noteFolderId,
            RelativePath = "note/index.html",
            IsFolder = false,
            SizeBytes = 0,
            ContentHash = emptyHash,
            RemoteUpdateTime = DateTime.UtcNow.AddHours(-1)
        });

        using var http = new HttpClient(new SyncHttpHandler(
            cloudFolderId,
            new Dictionary<Guid, byte[]?> { [remoteId] = Array.Empty<byte>() },
            new Dictionary<Guid, string> { [remoteId] = "index.html" },
            folderChildren: new Dictionary<Guid, List<Guid>>(),
            fileParents: new Dictionary<Guid, Guid>(),
            uploads: new List<Guid>(),
            nestedFolders: new Dictionary<string, (Guid FolderId, Guid FileId)>
            {
                ["note"] = (noteFolderId, remoteId)
            }))
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

        // Stub path: empty local downloads empty remote (non-null) and continues — must not abort.
        await engine.ReconcileAllAsync(new[] { mapping });

        var meta = store.GetAll(mappingId).Single(m => m.RemoteId == remoteId);
        Assert.Equal(emptyHash, meta.ContentHash);

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>
    /// Minimal GetEntities + getFile + addFile fake for SyncEngine reconcile soft-fail tests.
    /// byte[]? value null ⇒ getFile 404; empty array ⇒ 200 with zero body.
    /// </summary>
    private sealed class SyncHttpHandler : HttpMessageHandler
    {
        private readonly Guid _rootFolderId;
        private readonly Dictionary<Guid, byte[]?> _fileBytes;
        private readonly Dictionary<Guid, string> _fileNames;
        private readonly Dictionary<Guid, List<Guid>> _folderChildren;
        private readonly Dictionary<Guid, Guid> _fileParents;
        private readonly List<Guid> _uploads;
        private readonly Dictionary<string, (Guid FolderId, Guid FileId)> _nestedFolders;
        private readonly Dictionary<Guid, string> _folderNames = new();

        public SyncHttpHandler(
            Guid rootFolderId,
            Dictionary<Guid, byte[]?> fileBytes,
            Dictionary<Guid, string> fileNames,
            Dictionary<Guid, List<Guid>> folderChildren,
            Dictionary<Guid, Guid> fileParents,
            List<Guid> uploads,
            Dictionary<string, (Guid FolderId, Guid FileId)>? nestedFolders = null)
        {
            _rootFolderId = rootFolderId;
            _fileBytes = fileBytes;
            _fileNames = fileNames;
            _folderChildren = folderChildren;
            _fileParents = fileParents;
            _uploads = uploads;
            _nestedFolders = nestedFolders ?? new Dictionary<string, (Guid, Guid)>();
            foreach (var (name, pair) in _nestedFolders)
            {
                _folderNames[pair.FolderId] = name;
                _fileParents[pair.FileId] = pair.FolderId;
                _folderChildren[pair.FolderId] = new List<Guid> { pair.FileId };
            }
            if (_nestedFolders.Count > 0)
                _folderChildren[_rootFolderId] = _nestedFolders.Values.Select(v => v.FolderId).ToList();
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("GetEntities", StringComparison.OrdinalIgnoreCase))
            {
                var body = request.Content == null
                    ? ""
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                Guid? parentId = null;
                if (doc.RootElement.TryGetProperty("parentIds", out var parents)
                    && parents.ValueKind == JsonValueKind.Array
                    && parents.GetArrayLength() > 0)
                    parentId = parents[0].GetGuid();

                var list = new List<object>();
                if (parentId is Guid pid && _folderChildren.TryGetValue(pid, out var kids))
                {
                    foreach (var kid in kids)
                    {
                        if (_folderNames.TryGetValue(kid, out var folderName))
                        {
                            list.Add(EntityJson(kid, CloudEntityCodes.CloudFolder, folderName));
                            continue;
                        }
                        if (_fileNames.TryGetValue(kid, out var fileName))
                            list.Add(EntityJson(kid, CloudEntityCodes.CloudFile, fileName));
                    }
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(list), Encoding.UTF8, "application/json")
                };
            }

            if (path.Contains("getFile", StringComparison.OrdinalIgnoreCase))
            {
                var idText = path.Split('/').LastOrDefault() ?? "";
                if (!Guid.TryParse(idText, out var id) || !_fileBytes.TryGetValue(id, out var bytes))
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                if (bytes == null)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                };
            }

            if (path.Contains("addFile", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Content is MultipartFormDataContent multi)
                {
                    foreach (var part in multi)
                    {
                        if (part.Headers.ContentDisposition?.Name?.Trim('"') == "EntityId")
                        {
                            var s = await part.ReadAsStringAsync(cancellationToken);
                            if (Guid.TryParse(s, out var id))
                                _uploads.Add(id);
                        }
                    }
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            }

            if (path.Contains("AddEntity", StringComparison.OrdinalIgnoreCase))
            {
                var id = Guid.NewGuid();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{{\"id\":\"{id}\"}}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
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
