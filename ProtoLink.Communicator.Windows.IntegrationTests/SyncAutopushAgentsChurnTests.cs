using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Sync;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// AutoPush under mapped MyNotesFolder must settle while Agents role queues churn
/// (1.0.118: continuous Uploading… + LastSync missing for live map).
/// </summary>
[Trait("Category", "Offline")]
public class SyncAutopushAgentsChurnTests
{
    [Fact]
    public void ShouldSkipLocalPush_MatchesAgentsQueues_NotDocs()
    {
        Assert.True(AgentsRoleQueuePaths.ShouldSkipLocalPush(
            "Protolink.ru/Agents/roles/frontend-developer/todo/P0-x/index.html"));
        Assert.True(AgentsRoleQueuePaths.ShouldSkipLocalPush("Protolink.ru/Agents"));
        Assert.True(AgentsRoleQueuePaths.ShouldSkipLocalPush(
            "Protolink.ru/Agents/inbox/todo/ask/index.html"));
        Assert.False(AgentsRoleQueuePaths.ShouldSkipLocalPush("notes/keep.txt"));
        Assert.False(AgentsRoleQueuePaths.ShouldSkipLocalPush(
            "Protolink.ru/Agents/docs/ops/index.html"));
    }

    [Fact]
    public async Task PushLocal_AgentsQueueChurn_DoesNotUpload_AndStampsLastSync()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-agents-churn-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-agents-churn-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);

        var keepDir = Path.Combine(root, "notes");
        Directory.CreateDirectory(keepDir);
        var keepFile = Path.Combine(keepDir, "keep.txt");
        File.WriteAllText(keepFile, "stable-keep");

        var agentsTask = Path.Combine(
            root, "Protolink.ru", "Agents", "roles", "frontend-developer", "todo",
            "P0-churn-task", "index.html");
        Directory.CreateDirectory(Path.GetDirectoryName(agentsTask)!);
        File.WriteAllText(agentsTask, "agents-v1");

        var mappingId = Guid.NewGuid().ToString("N");
        var cloudFolderId = Guid.NewGuid();
        var keepRemote = Guid.NewGuid();
        var store = new JsonSyncMetadataStore(metaDir);
        // Warm meta (non-empty, has a file) so push does not upgrade to full reconcile.
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = Guid.NewGuid(),
            ParentRemoteId = cloudFolderId,
            RelativePath = "notes",
            IsFolder = true,
            SizeBytes = 0
        });
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = keepRemote,
            ParentRemoteId = cloudFolderId,
            RelativePath = "notes/keep.txt",
            IsFolder = false,
            SizeBytes = Encoding.UTF8.GetByteCount("stable-keep"),
            ContentHash = ContentHashUtil.Sha256Hex(Encoding.UTF8.GetBytes("stable-keep"))
        });

        var creates = new List<string>();
        using var http = new HttpClient(new CaptureCreateHandler(creates))
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

        // Continuous Agents dirty between Autopush ticks.
        for (var i = 0; i < 3; i++)
        {
            File.WriteAllText(agentsTask, "agents-v" + (i + 2));
            var applied = await engine.PushLocalChangesAsync(new[] { mapping });
            Assert.Equal(0, applied);
            Assert.DoesNotContain(creates, n => n.Contains("P0-churn-task", StringComparison.OrdinalIgnoreCase)
                || n.Contains("index.html", StringComparison.OrdinalIgnoreCase));
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(metaDir, "cloud-sync-metadata.json")));
        Assert.True(doc.RootElement.GetProperty("LastSync").TryGetProperty(mappingId, out _));

        // Snapshot with skip must omit Agents queue files (hash budget).
        var snap = SyncEngine.SnapshotFs(
            root,
            computeContentHashes: true,
            skipRelativePath: AgentsRoleQueuePaths.ShouldSkipLocalPush);
        Assert.Contains(snap, e => !e.IsFolder && e.RelativePath.Replace('\\', '/') == "notes/keep.txt");
        Assert.DoesNotContain(snap, e => e.RelativePath.Contains("P0-churn-task", StringComparison.OrdinalIgnoreCase));

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task PushLocal_BudgetCancel_StillStampsLastSync()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-push-budget-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-push-budget-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        File.WriteAllText(Path.Combine(root, "dirty.txt"), "dirty-v2-longer");

        var mappingId = Guid.NewGuid().ToString("N");
        var cloudFolderId = Guid.NewGuid();
        var dirtyRemote = Guid.NewGuid();
        var store = new JsonSyncMetadataStore(metaDir);
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = dirtyRemote,
            ParentRemoteId = cloudFolderId,
            RelativePath = "dirty.txt",
            IsFolder = false,
            SizeBytes = 1,
            ContentHash = "aa"
        });

        using var http = new HttpClient(new HangAddFileHandler())
        {
            BaseAddress = new Uri("http://sync-test.local/")
        };
        var engine = new SyncEngine(store, new CloudApiService(http));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.PushLocalChangesAsync(
                new[]
                {
                    new SyncMappingInfo
                    {
                        Id = mappingId,
                        CloudFolderId = cloudFolderId,
                        LocalRootPath = root
                    }
                },
                cts.Token));

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(metaDir, "cloud-sync-metadata.json")));
        Assert.True(
            doc.RootElement.GetProperty("LastSync").TryGetProperty(mappingId, out _),
            "Budget cancel must still stamp LastSync for the mapping that started push");

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    private sealed class CaptureCreateHandler : HttpMessageHandler
    {
        private readonly List<string> _names;

        public CaptureCreateHandler(List<string> names) => _names = names;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("AddEntity", StringComparison.OrdinalIgnoreCase)
                || path.Contains("addValue", StringComparison.OrdinalIgnoreCase)
                || path.Contains("AddValue", StringComparison.OrdinalIgnoreCase))
            {
                var body = request.Content == null
                    ? ""
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                _names.Add(body);
                var id = Guid.NewGuid();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{{\"id\":\"{id}\"}}", Encoding.UTF8, "application/json")
                };
            }

            if (path.Contains("addFile", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class HangAddFileHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("addFile", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }
}
