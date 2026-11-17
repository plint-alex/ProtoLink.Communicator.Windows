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

    [Theory]
    [InlineData(100L, 100L, "abc", null, null, true)]
    [InlineData(100L, 100L, "abc", "2026-11-16T12:00:00Z", "2026-11-16T12:00:00Z", true)]
    [InlineData(100L, 100L, "abc", "2026-11-16T13:00:00Z", "2026-11-16T12:00:00Z", false)]
    [InlineData(100L, 99L, "abc", "2026-11-16T12:00:00Z", "2026-11-16T12:00:00Z", false)]
    [InlineData(100L, 100L, "", "2026-11-16T12:00:00Z", "2026-11-16T12:00:00Z", false)]
    public void CanSkipCompareWithoutHash_MatchesContract(
        long localSize,
        long metaSize,
        string metaHash,
        string? remoteIso,
        string? metaIso,
        bool expected)
    {
        DateTime? remote = remoteIso == null ? null : DateTime.Parse(remoteIso, null, System.Globalization.DateTimeStyles.RoundtripKind);
        DateTime? meta = metaIso == null ? null : DateTime.Parse(metaIso, null, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.Equal(
            expected,
            ContentHashUtil.CanSkipCompareWithoutHash(localSize, metaSize, metaHash, remote, meta));
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
    public async Task Reconcile_RemoteNewer_ViaValuesRelist_DownloadsAndApplies()
    {
        // Warm values-light ignores structure-only UpdateTime; force IncludeValues re-list with an
        // unknown sibling so listing UpdateTime applies (cloud-wins path).
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
        var unknownRemoteId = Guid.NewGuid();
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
            cloudFolderId, fileId, "note.html", remoteTime, Encoding.UTF8.GetBytes(remoteBody),
            unknownSiblingId: unknownRemoteId, unknownSiblingName: "extra-remote.html");
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

        Assert.True(handler.ParentListCallsWithValues >= 1);
        Assert.True(handler.GetFileCalls >= 1);
        Assert.Equal(remoteBody, File.ReadAllText(Path.Combine(root, "note.html")));

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>
    /// 1.0.77 hang class: warm values-light listing UpdateTime newer than meta must NOT force
    /// getFile for unchanged local+meta (otherwise ~10k downloads starve SetLastSyncUtc).
    /// </summary>
    [Fact]
    public async Task Reconcile_WarmValuesLight_NewerListingTime_DoesNotGetFile_AndStampsLastSync()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-cmp-light-newer-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-cmp-light-newer-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        const string body = "stable-note-body-xxxxxxxx";
        File.WriteAllText(Path.Combine(root, "note.html"), body);
        var hash = ContentHashUtil.Sha256Hex(Encoding.UTF8.GetBytes(body));
        var metaTime = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        var listingNewer = metaTime.AddHours(2);

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
            RemoteUpdateTime = metaTime
        });

        var handler = new CountingGetFileHandler(cloudFolderId, fileId, "note.html", listingNewer);
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

        Assert.True(handler.ParentListCallsWithoutValues >= 1);
        Assert.Equal(0, handler.ParentListCallsWithValues);
        Assert.Equal(0, handler.GetFileCalls);
        var metaPath = Path.Combine(metaDir, "cloud-sync-metadata.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
        Assert.True(doc.RootElement.GetProperty("LastSync").TryGetProperty(mappingId, out var ls));
        Assert.True(ls.GetDateTime() > metaTime);

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task DownloadBytes_Timeout_SoftFails_AndReconcileStampsLastSync()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-cmp-dl-timeout-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-cmp-dl-timeout-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        // Divergent meta hash forces getFile after local hash (hangs until CancelAfter soft-fail).
        const string body = "local-bytes-for-timeout-xx";
        File.WriteAllText(Path.Combine(root, "note.html"), body);
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
            // Empty baseline → cannot skip compare/getFile (forces DownloadBytes soft-fail path).
            ContentHash = "",
            RemoteUpdateTime = stamped
        });

        var handler = new CountingGetFileHandler(
            cloudFolderId, fileId, "note.html", stamped, hangGetFile: true);
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://sync-test.local/"),
            Timeout = TimeSpan.FromMinutes(5)
        };
        var engine = new SyncEngine(store, new CloudApiService(http));
        var priorBudget = SyncEngine.RemoteDownloadBudget;
        SyncEngine.RemoteDownloadBudget = TimeSpan.FromSeconds(2);
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await engine.ReconcileAllAsync(new[]
            {
                new SyncMappingInfo
                {
                    Id = mappingId,
                    CloudFolderId = cloudFolderId,
                    LocalRootPath = root
                }
            });
            sw.Stop();

            Assert.True(handler.GetFileCalls >= 1);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"elapsed {sw.Elapsed} — budget soft-fail should win");
            var metaPath = Path.Combine(metaDir, "cloud-sync-metadata.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
            Assert.True(doc.RootElement.GetProperty("LastSync").TryGetProperty(mappingId, out _));
        }
        finally
        {
            SyncEngine.RemoteDownloadBudget = priorBudget;
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
            try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void DevTools_OmitsBodiesForFileTransferAndGetEntities()
    {
        using var get = new HttpRequestMessage(HttpMethod.Get, "https://protolink.ru/api/Files/getFile/" + Guid.NewGuid());
        using var add = new HttpRequestMessage(HttpMethod.Post, "https://protolink.ru/api/Files/addFile");
        using var entities = new HttpRequestMessage(HttpMethod.Post, "https://protolink.ru/api/Entities/GetEntities");
        using var version = new HttpRequestMessage(HttpMethod.Get, "https://protolink.ru/api/Version");
        Assert.True(DevToolsLoggingHandler.ShouldOmitBodyForLogging(get));
        Assert.True(DevToolsLoggingHandler.ShouldOmitBodyForLogging(add));
        // WalkRemote floods GetEntities — must omit like getFile (1.0.74 quiet-meta hang class).
        Assert.True(DevToolsLoggingHandler.ShouldOmitBodyForLogging(entities));
        Assert.True(DevToolsLoggingHandler.IsBinaryFileTransfer(entities));
        Assert.False(DevToolsLoggingHandler.ShouldOmitBodyForLogging(version));
    }

    [Fact]
    public void SnapshotFs_ReconcilePath_SkipsContentHashes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-snap-skip-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "note.html");
        File.WriteAllText(path, "snapshot-size-only-body");
        try
        {
            var withHash = SyncEngine.SnapshotFs(root, computeContentHashes: true);
            var fileWith = Assert.Single(withHash, e => !e.IsFolder);
            Assert.False(string.IsNullOrEmpty(fileWith.ContentHash));

            var sizeOnly = SyncEngine.SnapshotFs(root, computeContentHashes: false);
            var fileOnly = Assert.Single(sizeOnly, e => !e.IsFolder);
            Assert.Equal(fileWith.SizeBytes, fileOnly.SizeBytes);
            Assert.Equal("", fileOnly.ContentHash);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Warm meta WalkRemote must list ParentIds with IncludeValues=false (1.0.75 hang class).
    /// </summary>
    [Fact]
    public async Task Reconcile_WarmMeta_WalkRemote_ListsWithoutValues()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-walk-light-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-walk-light-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        const string body = "warm-walk-note-body-xxxx";
        File.WriteAllText(Path.Combine(root, "note.html"), body);
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

        Assert.True(handler.ParentListCallsWithoutValues >= 1);
        Assert.Equal(0, handler.ParentListCallsWithValues);
        Assert.Equal(0, handler.GetFileCalls);

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    private sealed class CountingGetFileHandler : HttpMessageHandler
    {
        private readonly Guid _folderId;
        private readonly Guid _fileId;
        private readonly string _fileName;
        private readonly DateTime _updateTime;
        private readonly byte[] _remoteBytes;
        private readonly Guid? _unknownSiblingId;
        private readonly string? _unknownSiblingName;
        private readonly bool _hangGetFile;

        public int GetFileCalls { get; private set; }
        public int ParentListCallsWithValues { get; private set; }
        public int ParentListCallsWithoutValues { get; private set; }

        public CountingGetFileHandler(
            Guid folderId,
            Guid fileId,
            string fileName,
            DateTime updateTime,
            byte[]? remoteBytes = null,
            Guid? unknownSiblingId = null,
            string? unknownSiblingName = null,
            bool hangGetFile = false)
        {
            _folderId = folderId;
            _fileId = fileId;
            _fileName = fileName;
            _updateTime = updateTime;
            _remoteBytes = remoteBytes ?? Encoding.UTF8.GetBytes("should-not-download");
            _unknownSiblingId = unknownSiblingId;
            _unknownSiblingName = unknownSiblingName;
            _hangGetFile = hangGetFile;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("GetEntities", StringComparison.OrdinalIgnoreCase))
            {
                var body = request.Content == null
                    ? "{}"
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var root = doc.RootElement;
                var includeValues = root.TryGetProperty("includeValues", out var iv) && iv.GetBoolean();
                var hasParents = root.TryGetProperty("parentIds", out var parentsEl)
                                 && parentsEl.ValueKind == JsonValueKind.Array
                                 && parentsEl.GetArrayLength() > 0;

                if (hasParents)
                {
                    if (includeValues) ParentListCallsWithValues++;
                    else ParentListCallsWithoutValues++;
                }

                var entities = new List<object>
                {
                    new
                    {
                        id = _fileId,
                        code = CloudEntityCodes.CloudFile,
                        version = 1,
                        creationTime = _updateTime,
                        updateTime = _updateTime,
                        values = includeValues
                            ? new object[]
                            {
                                new
                                {
                                    id = Guid.NewGuid(),
                                    type = "String",
                                    value = _fileName,
                                    parents = new[] { NameTypeId }
                                }
                            }
                            : Array.Empty<object>()
                    }
                };

                if (_unknownSiblingId is Guid extraId)
                {
                    entities.Add(new
                    {
                        id = extraId,
                        code = CloudEntityCodes.CloudFile,
                        version = 1,
                        creationTime = _updateTime,
                        updateTime = _updateTime,
                        values = includeValues
                            ? new object[]
                            {
                                new
                                {
                                    id = Guid.NewGuid(),
                                    type = "String",
                                    value = _unknownSiblingName ?? "extra.html",
                                    parents = new[] { NameTypeId }
                                }
                            }
                            : Array.Empty<object>()
                    });
                }

                var payload = JsonSerializer.Serialize(entities);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
            }

            if (path.Contains("getFile", StringComparison.OrdinalIgnoreCase))
            {
                GetFileCalls++;
                if (_hangGetFile)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_remoteBytes)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }
}
