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
/// ApplyComparePhase must not re-download every file when local still matches meta and remote
/// listing is not newer — otherwise deferred full Sync after autopush preempt never stamps LastSync
/// on ~10k MyNotesFolder trees (tester FAIL 1.0.72 sticky Syncing).
/// </summary>
[Trait("Category", "Offline")]
public class SyncCompareSkipUnchangedTests
{
    private static readonly Guid NameTypeId = CloudEntityCodes.NameTypeId;

    [Theory]
    [InlineData("abc", "abc", null, null, true)]
    [InlineData("abc", "abc", "2026-11-16T12:00:00Z", "2026-11-16T12:00:00Z", true)]
    [InlineData("abc", "abc", "2026-11-16T11:00:00Z", "2026-11-16T12:00:00Z", true)]
    [InlineData("abc", "abc", "2026-11-16T13:00:00Z", "2026-11-16T12:00:00Z", false)]
    [InlineData("abc", "xyz", "2026-11-16T12:00:00Z", "2026-11-16T12:00:00Z", false)]
    [InlineData("abc", "", "2026-11-16T12:00:00Z", "2026-11-16T12:00:00Z", false)]
    public void CanSkipRemoteContentDownload_MatchesContract(
        string localHash,
        string metaHash,
        string? remoteIso,
        string? metaIso,
        bool expected)
    {
        DateTime? remote = remoteIso == null ? null : DateTime.Parse(remoteIso, null, System.Globalization.DateTimeStyles.RoundtripKind);
        DateTime? meta = metaIso == null ? null : DateTime.Parse(metaIso, null, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.Equal(
            expected,
            ContentHashUtil.CanSkipRemoteContentDownload(localHash, metaHash, remote, meta));
    }

    [Fact]
    public async Task Reconcile_UnchangedFile_DoesNotCallGetFile_AndStampsLastSync()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-cmp-skip-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-cmp-skip-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        const string body = "stable-note-body-xxxxxxxx";
        var filePath = Path.Combine(root, "note.html");
        File.WriteAllText(filePath, body);
        var hash = ContentHashUtil.Sha256Hex(Encoding.UTF8.GetBytes(body));
        var stamped = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

        var mappingId = Guid.NewGuid().ToString("N");
        var cloudFolderId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var store = new JsonSyncMetadataStore(metaDir);
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = fileId,
            ParentRemoteId = cloudFolderId,
            RelativePath = "note.html",
            IsFolder = false,
            SizeBytes = body.Length,
            ContentHash = hash,
            RemoteUpdateTime = stamped
        });

        var handler = new CountingGetFileHandler(cloudFolderId, fileId, "note.html", stamped);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://sync-test.local/") };
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

        Assert.Equal(0, handler.GetFileCalls);
        var metaPath = Path.Combine(metaDir, "cloud-sync-metadata.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
        Assert.True(doc.RootElement.GetProperty("LastSync").TryGetProperty(mappingId, out var ls));
        Assert.True(ls.GetDateTime() > stamped);

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Reconcile_RemoteNewer_DownloadsAndApplies()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-cmp-newer-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-cmp-newer-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        const string localBody = "local-old-body-xxxxxxxx";
        const string remoteBody = "remote-new-body-xxxxxxx";
        File.WriteAllText(Path.Combine(root, "note.html"), localBody);
        var localHash = ContentHashUtil.Sha256Hex(Encoding.UTF8.GetBytes(localBody));
        var metaTime = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        var remoteTime = metaTime.AddHours(1);

        var mappingId = Guid.NewGuid().ToString("N");
        var cloudFolderId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var store = new JsonSyncMetadataStore(metaDir);
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = fileId,
            ParentRemoteId = cloudFolderId,
            RelativePath = "note.html",
            IsFolder = false,
            SizeBytes = localBody.Length,
            ContentHash = localHash,
            RemoteUpdateTime = metaTime
        });

        var handler = new CountingGetFileHandler(
            cloudFolderId, fileId, "note.html", remoteTime, Encoding.UTF8.GetBytes(remoteBody));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://sync-test.local/") };
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

        Assert.True(handler.GetFileCalls >= 1);
        Assert.Equal(remoteBody, File.ReadAllText(Path.Combine(root, "note.html")));

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void DevTools_SkipsBinaryFileTransferPaths()
    {
        using var get = new HttpRequestMessage(HttpMethod.Get, "https://protolink.ru/api/Files/getFile/" + Guid.NewGuid());
        using var add = new HttpRequestMessage(HttpMethod.Post, "https://protolink.ru/api/Files/addFile");
        using var entities = new HttpRequestMessage(HttpMethod.Post, "https://protolink.ru/api/Entities/GetEntities");
        Assert.True(DevToolsLoggingHandler.IsBinaryFileTransfer(get));
        Assert.True(DevToolsLoggingHandler.IsBinaryFileTransfer(add));
        Assert.False(DevToolsLoggingHandler.IsBinaryFileTransfer(entities));
    }

    private sealed class CountingGetFileHandler : HttpMessageHandler
    {
        private readonly Guid _folderId;
        private readonly Guid _fileId;
        private readonly string _fileName;
        private readonly DateTime _updateTime;
        private readonly byte[] _remoteBytes;

        public int GetFileCalls { get; private set; }

        public CountingGetFileHandler(
            Guid folderId,
            Guid fileId,
            string fileName,
            DateTime updateTime,
            byte[]? remoteBytes = null)
        {
            _folderId = folderId;
            _fileId = fileId;
            _fileName = fileName;
            _updateTime = updateTime;
            _remoteBytes = remoteBytes ?? Encoding.UTF8.GetBytes("should-not-download");
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("GetEntities", StringComparison.OrdinalIgnoreCase))
            {
                var payload = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        id = _fileId,
                        code = CloudEntityCodes.CloudFile,
                        version = 1,
                        creationTime = _updateTime,
                        updateTime = _updateTime,
                        values = new[]
                        {
                            new
                            {
                                id = Guid.NewGuid(),
                                type = "String",
                                value = _fileName,
                                parents = new[] { NameTypeId }
                            }
                        }
                    }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                });
            }

            if (path.Contains("getFile", StringComparison.OrdinalIgnoreCase))
            {
                GetFileCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_remoteBytes)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
