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
/// Near-empty file inventory (folders + few residual file rows) must heal like folders-only —
/// live 1.0.119 sticky Syncing… on c01a45a2 with 317 folders + 2 files vs local ~18k.
/// </summary>
[Trait("Category", "Offline")]
public class SyncMetaFileCollapseHealTests
{
    [Theory]
    [InlineData(317, 2, true)]
    [InlineData(176, 152, false)]
    [InlineData(10, 1, true)]
    [InlineData(7, 1, false)]
    [InlineData(32, 0, true)]
    public void IsNearEmptyFileInventory_MatchesCollapseSignature(int folders, int files, bool expect)
    {
        Assert.Equal(expect, SyncMetaInventoryHeal.IsNearEmptyFileInventory(folders, files));
    }

    [Fact]
    public void NeedsRemoteBootstrap_NearEmpty_WithLargeLocal_True()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-meta-collapse-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        for (var i = 0; i < 20; i++)
            File.WriteAllText(Path.Combine(root, $"local-{i}.txt"), "x");

        var meta = new List<SyncItemMeta>();
        for (var i = 0; i < 12; i++)
        {
            meta.Add(new SyncItemMeta
            {
                MappingId = "m",
                RemoteId = Guid.NewGuid(),
                ParentRemoteId = Guid.NewGuid(),
                RelativePath = $"folder-{i}",
                IsFolder = true
            });
        }
        meta.Add(new SyncItemMeta
        {
            MappingId = "m",
            RemoteId = Guid.NewGuid(),
            ParentRemoteId = Guid.NewGuid(),
            RelativePath = "folder-0/a.txt",
            IsFolder = false
        });
        meta.Add(new SyncItemMeta
        {
            MappingId = "m",
            RemoteId = Guid.NewGuid(),
            ParentRemoteId = Guid.NewGuid(),
            RelativePath = "folder-0/b.txt",
            IsFolder = false
        });

        Assert.True(SyncMetaInventoryHeal.NeedsRemoteBootstrap(meta, root));

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void NeedsRemoteBootstrap_NearEmpty_ButTinyLocal_False()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-meta-collapse-tiny-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "only.txt"), "x");

        var meta = new List<SyncItemMeta>();
        for (var i = 0; i < 12; i++)
        {
            meta.Add(new SyncItemMeta
            {
                MappingId = "m",
                RemoteId = Guid.NewGuid(),
                ParentRemoteId = Guid.NewGuid(),
                RelativePath = $"folder-{i}",
                IsFolder = true
            });
        }
        meta.Add(new SyncItemMeta
        {
            MappingId = "m",
            RemoteId = Guid.NewGuid(),
            ParentRemoteId = Guid.NewGuid(),
            RelativePath = "only.txt",
            IsFolder = false
        });
        meta.Add(new SyncItemMeta
        {
            MappingId = "m",
            RemoteId = Guid.NewGuid(),
            ParentRemoteId = Guid.NewGuid(),
            RelativePath = "ghost.txt",
            IsFolder = false
        });

        Assert.False(SyncMetaInventoryHeal.NeedsRemoteBootstrap(meta, root));

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Reconcile_NearEmptyFileMeta_HealsBootstrap_AndStampsLastSync_WithoutAgentsUpload()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-meta-collapse-rec-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-meta-collapse-rec-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        Directory.CreateDirectory(Path.Combine(root, "films"));

        // Local Agents churn that must not block Sync settle (ApplyLocal exclude).
        var agentsTodo = Path.Combine(
            root, "Protolink.ru", "Agents", "roles", "frontend-developer", "todo");
        for (var i = 0; i < 24; i++)
        {
            var taskDir = Path.Combine(agentsTodo, $"P0-churn-{i}");
            Directory.CreateDirectory(taskDir);
            File.WriteAllText(Path.Combine(taskDir, "index.html"), "agents-churn-" + i);
        }

        var mappingId = Guid.NewGuid().ToString("N");
        var cloudFolderId = Guid.NewGuid();
        var filmsFolderId = Guid.NewGuid();
        var filmsFileId = Guid.NewGuid();
        var residualFileId = Guid.NewGuid();
        var store = new JsonSyncMetadataStore(metaDir);

        // Folder-rich + 2 residual file rows — strict folders-only gate would skip heal.
        for (var i = 0; i < 10; i++)
        {
            var folderId = Guid.NewGuid();
            store.Upsert(new SyncItemMeta
            {
                MappingId = mappingId,
                RemoteId = folderId,
                ParentRemoteId = cloudFolderId,
                RelativePath = $"shell-{i}",
                IsFolder = true,
                SizeBytes = 0
            });
            Directory.CreateDirectory(Path.Combine(root, $"shell-{i}"));
        }
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = filmsFolderId,
            ParentRemoteId = cloudFolderId,
            RelativePath = "films",
            IsFolder = true,
            SizeBytes = 0
        });
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = residualFileId,
            ParentRemoteId = filmsFolderId,
            RelativePath = "films/residual.txt",
            IsFolder = false,
            SizeBytes = 1,
            ContentHash = "aa"
        });
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = Guid.NewGuid(),
            ParentRemoteId = filmsFolderId,
            RelativePath = "films/other-residual.txt",
            IsFolder = false,
            SizeBytes = 1,
            ContentHash = "bb"
        });

        var before = store.GetAll(mappingId);
        Assert.False(before.All(m => m.IsFolder), "fixture must keep residual files so folders-only gate alone would miss");
        Assert.True(SyncMetaInventoryHeal.NeedsRemoteBootstrap(before, root));

        const string cloudBody = "collapse-heal-body";
        var creates = new List<string>();
        using var http = new HttpClient(new CollapseHealHttpHandler(
            cloudFolderId,
            filmsFolderId,
            filmsFileId,
            Encoding.UTF8.GetBytes(cloudBody),
            creates))
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
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(metaDir, "cloud-sync-metadata.json")));
        Assert.True(
            doc.RootElement.GetProperty("LastSync").TryGetProperty(mappingId, out _),
            "Reconcile must stamp LastSync (leave Syncing…) after near-empty heal");
        Assert.DoesNotContain(creates, n => n.Contains("P0-churn-", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(creates, n => n.Contains("Agents", StringComparison.OrdinalIgnoreCase));

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task PushLocal_NearEmptyFileMeta_UpgradesToFullReconcile()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-meta-collapse-push-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-meta-collapse-push-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        Directory.CreateDirectory(Path.Combine(root, "films"));
        for (var i = 0; i < 20; i++)
            File.WriteAllText(Path.Combine(root, $"extra-{i}.txt"), "x");

        var mappingId = Guid.NewGuid().ToString("N");
        var cloudFolderId = Guid.NewGuid();
        var filmsFolderId = Guid.NewGuid();
        var filmsFileId = Guid.NewGuid();
        var store = new JsonSyncMetadataStore(metaDir);
        for (var i = 0; i < 10; i++)
        {
            store.Upsert(new SyncItemMeta
            {
                MappingId = mappingId,
                RemoteId = Guid.NewGuid(),
                ParentRemoteId = cloudFolderId,
                RelativePath = $"shell-{i}",
                IsFolder = true
            });
            Directory.CreateDirectory(Path.Combine(root, $"shell-{i}"));
        }
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = filmsFolderId,
            ParentRemoteId = cloudFolderId,
            RelativePath = "films",
            IsFolder = true
        });
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = Guid.NewGuid(),
            ParentRemoteId = filmsFolderId,
            RelativePath = "films/residual-a.txt",
            IsFolder = false,
            SizeBytes = 1
        });
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = Guid.NewGuid(),
            ParentRemoteId = filmsFolderId,
            RelativePath = "films/residual-b.txt",
            IsFolder = false,
            SizeBytes = 1
        });

        const string cloudBody = "push-collapse-heal";
        using var http = new HttpClient(new CollapseHealHttpHandler(
            cloudFolderId,
            filmsFolderId,
            filmsFileId,
            Encoding.UTF8.GetBytes(cloudBody),
            new List<string>()))
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

    private sealed class CollapseHealHttpHandler : HttpMessageHandler
    {
        private readonly Guid _rootFolderId;
        private readonly Guid _filmsFolderId;
        private readonly Guid _filmsFileId;
        private readonly byte[] _fileBytes;
        private readonly List<string> _creates;

        public CollapseHealHttpHandler(
            Guid rootFolderId,
            Guid filmsFolderId,
            Guid filmsFileId,
            byte[] fileBytes,
            List<string> creates)
        {
            _rootFolderId = rootFolderId;
            _filmsFolderId = filmsFolderId;
            _filmsFileId = filmsFileId;
            _fileBytes = fileBytes;
            _creates = creates;
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
                if (Guid.TryParse(idText, out var id) && id == _filmsFileId)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(_fileBytes)
                    });
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
            if (path.Contains("AddEntity", StringComparison.OrdinalIgnoreCase)
                || path.Contains("AddValue", StringComparison.OrdinalIgnoreCase)
                || path.Contains("addValue", StringComparison.OrdinalIgnoreCase))
            {
                var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                _creates.Add(body);
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
                list.Add(EntityJson(_filmsFolderId, CloudEntityCodes.CloudFolder, "films"));
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
                    parents = new[] { CloudEntityCodes.NameTypeId }
                }
            }
        };
    }
}
