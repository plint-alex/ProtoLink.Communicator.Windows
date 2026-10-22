using System.Net;
using System.Net.Http;
using System.Text;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Sync;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>NFR-SYNC-1: missing intermediate directory during hash/apply must soft-fail per item.</summary>
[Trait("Category", "Offline")]
public class SyncMissingLocalPathTests
{
    [Fact]
    public void IsMissing_OnlyNotFoundClasses()
    {
        Assert.True(SyncMissingLocalPath.IsMissing(new FileNotFoundException("x")));
        Assert.True(SyncMissingLocalPath.IsMissing(new DirectoryNotFoundException("x")));
        Assert.False(SyncMissingLocalPath.IsMissing(new IOException("sharing violation")));
        Assert.False(SyncMissingLocalPath.IsMissing(new UnauthorizedAccessException()));
        Assert.False(SyncMissingLocalPath.IsMissing(new InvalidOperationException()));
    }

    [Fact]
    public void TrySha256HexFile_MissingIntermediateDirectory_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-sync-miss-" + Guid.NewGuid().ToString("N"));
        var missing = Path.Combine(root, "progress", "gone folder", "index.html");
        Assert.Null(ContentHashUtil.TrySha256HexFile(missing));
    }

    [Fact]
    public void TrySha256HexFile_ExistingFile_ReturnsHex()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-sync-ok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "index.html");
        try
        {
            File.WriteAllText(file, "<h1>ok</h1>\n");
            var hash = ContentHashUtil.TrySha256HexFile(file);
            Assert.NotNull(hash);
            Assert.Equal(64, hash!.Length);
            Assert.Equal(ContentHashUtil.Sha256HexFile(file), hash);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Sha256HexFile_MissingIntermediateDirectory_ThrowsDirectoryNotFound()
    {
        var missing = Path.Combine(Path.GetTempPath(), "pl-no-dir-" + Guid.NewGuid().ToString("N"), "a", "b.html");
        Assert.ThrowsAny<DirectoryNotFoundException>(() => ContentHashUtil.Sha256HexFile(missing));
    }

    [Fact]
    public void SnapshotFs_OmitsGoneFile_KeepsSibling()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-snap-" + Guid.NewGuid().ToString("N"));
        var keepDir = Path.Combine(root, "keep");
        var goneDir = Path.Combine(root, "progress", "i see an exception");
        Directory.CreateDirectory(keepDir);
        Directory.CreateDirectory(goneDir);
        File.WriteAllText(Path.Combine(keepDir, "index.html"), "keep");
        var goneFile = Path.Combine(goneDir, "index.html");
        File.WriteAllText(goneFile, "gone");
        try
        {
            // Simulate mid-pass delete of the mapped progress folder (inbox exception).
            Directory.Delete(Path.Combine(root, "progress"), recursive: true);
            var snap = SyncEngine.SnapshotFs(root);
            Assert.Contains(snap, e => !e.IsFolder && e.RelativePath.Replace('\\', '/') == "keep/index.html");
            Assert.DoesNotContain(snap, e => e.RelativePath.Contains("i see an exception", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task PushLocalChanges_MissingUpdatedPath_DoesNotAbortSiblingUpload()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-push-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        var keepDir = Path.Combine(root, "keep");
        var goneDir = Path.Combine(root, "progress", "i see an exception");
        Directory.CreateDirectory(keepDir);
        Directory.CreateDirectory(goneDir);
        File.WriteAllText(Path.Combine(keepDir, "index.html"), "keep-v2-longer");
        File.WriteAllText(Path.Combine(goneDir, "index.html"), "gone-v2-longer");

        var mappingId = Guid.NewGuid().ToString("N");
        var cloudFolderId = Guid.NewGuid();
        var keepRemote = Guid.NewGuid();
        var goneRemote = Guid.NewGuid();
        var store = new JsonSyncMetadataStore(metaDir);
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = keepRemote,
            ParentRemoteId = cloudFolderId,
            RelativePath = "keep/index.html",
            IsFolder = false,
            SizeBytes = 1,
            ContentHash = "aa"
        });
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = goneRemote,
            ParentRemoteId = cloudFolderId,
            RelativePath = "progress/i see an exception/index.html",
            IsFolder = false,
            SizeBytes = 1,
            ContentHash = "bb"
        });

        // Race: classifier would have seen both; delete before ApplyLocalPhase hash/upload.
        Directory.Delete(Path.Combine(root, "progress"), recursive: true);

        var uploads = new List<Guid>();
        var deletes = new List<Guid>();
        using var http = new HttpClient(new RecordingHandler(uploads, deletes))
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

        var applied = await engine.PushLocalChangesAsync(new[] { mapping });

        Assert.Contains(keepRemote, uploads);
        Assert.Contains(goneRemote, deletes);
        Assert.True(applied >= 1);
        Assert.DoesNotContain(goneRemote, uploads);
        var keepMeta = store.GetAll(mappingId).Single(m => m.RemoteId == keepRemote);
        Assert.True(keepMeta.SizeBytes > 1);
        Assert.False(string.IsNullOrEmpty(keepMeta.ContentHash));
        Assert.DoesNotContain(store.GetAll(mappingId), m => m.RemoteId == goneRemote);

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>
    /// Snapshot sees both Updated files; an earlier Added upload deletes the progress folder
    /// so ApplyLocalPhase.Updated hits DirectoryNotFound on hash/open — must soft-fail and still upload keep.
    /// </summary>
    [Fact]
    public async Task PushLocalChanges_UpdatedFileParentDeletedMidPass_SoftFailsAndContinues()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-mid-" + Guid.NewGuid().ToString("N"));
        var metaDir = Path.Combine(Path.GetTempPath(), "pl-mid-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(metaDir);
        var keepDir = Path.Combine(root, "keep");
        var goneDir = Path.Combine(root, "progress", "i see an exception");
        var triggerDir = Path.Combine(root, "trigger");
        Directory.CreateDirectory(keepDir);
        Directory.CreateDirectory(goneDir);
        Directory.CreateDirectory(triggerDir);
        File.WriteAllText(Path.Combine(keepDir, "index.html"), "keep-body-v2");
        File.WriteAllText(Path.Combine(goneDir, "index.html"), "gone-body-v2");
        File.WriteAllText(Path.Combine(triggerDir, "new.html"), "trigger-upload");

        var mappingId = Guid.NewGuid().ToString("N");
        var cloudFolderId = Guid.NewGuid();
        var keepRemote = Guid.NewGuid();
        var goneRemote = Guid.NewGuid();
        var store = new JsonSyncMetadataStore(metaDir);
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = keepRemote,
            ParentRemoteId = cloudFolderId,
            RelativePath = "keep/index.html",
            IsFolder = false,
            SizeBytes = 1,
            ContentHash = "aa"
        });
        store.Upsert(new SyncItemMeta
        {
            MappingId = mappingId,
            RemoteId = goneRemote,
            ParentRemoteId = cloudFolderId,
            RelativePath = "progress/i see an exception/index.html",
            IsFolder = false,
            SizeBytes = 1,
            ContentHash = "bb"
        });

        var uploads = new List<Guid>();
        var deletes = new List<Guid>();
        var progressDeleted = false;
        var handler = new RecordingHandler(uploads, deletes, onAddFile: _ =>
        {
            if (progressDeleted) return;
            var progress = Path.Combine(root, "progress");
            if (Directory.Exists(progress))
            {
                Directory.Delete(progress, recursive: true);
                progressDeleted = true;
            }
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://sync-test.local/") };
        var engine = new SyncEngine(store, new CloudApiService(http));
        var mapping = new SyncMappingInfo
        {
            Id = mappingId,
            CloudFolderId = cloudFolderId,
            LocalRootPath = root
        };

        var applied = await engine.PushLocalChangesAsync(new[] { mapping });

        Assert.True(progressDeleted, "trigger Added must delete progress before Updated phase finishes");
        Assert.Contains(keepRemote, uploads);
        Assert.True(applied >= 1);
        Assert.DoesNotContain(goneRemote, uploads);

        try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(metaDir, recursive: true); } catch { /* ignore */ }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<Guid> _uploads;
        private readonly List<Guid> _deletes;
        private readonly Action<Guid>? _onAddFile;

        public RecordingHandler(List<Guid> uploads, List<Guid> deletes, Action<Guid>? onAddFile = null)
        {
            _uploads = uploads;
            _deletes = deletes;
            _onAddFile = onAddFile;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("addFile", StringComparison.OrdinalIgnoreCase))
            {
                // Multipart: EntityId field
                var body = request.Content == null
                    ? ""
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                var id = Guid.Empty;
                // EntityId may be in multipart; also try form parse from raw
                if (request.Content is MultipartFormDataContent multi)
                {
                    foreach (var part in multi)
                    {
                        if (part.Headers.ContentDisposition?.Name?.Trim('"') == "EntityId")
                        {
                            var s = await part.ReadAsStringAsync(cancellationToken);
                            Guid.TryParse(s, out id);
                        }
                    }
                }
                else
                {
                    // Fallback: Content already consumed as string — EntityId=guid patterns rare
                    _ = body;
                }
                if (id != Guid.Empty)
                {
                    _onAddFile?.Invoke(id);
                    _uploads.Add(id);
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            }

            if (path.Contains("DeleteEntity", StringComparison.OrdinalIgnoreCase))
            {
                var json = request.Content == null
                    ? ""
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                var match = System.Text.RegularExpressions.Regex.Match(
                    json, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                if (match.Success && Guid.TryParse(match.Value, out var del))
                    _deletes.Add(del);
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
    }
}
