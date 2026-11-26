using System.IO;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;

namespace ProtoLink.Communicator.Windows.Services.Sync;

/// <summary>3-way reconcile: FS ↔ metadata store ↔ remote. Cloud-first materialize; mirrors Android SyncEngine.</summary>
public sealed partial class SyncEngine
{
    private readonly JsonSyncMetadataStore _store;
    private readonly CloudApiService _api;
    private readonly LocalChangeClassifier _localClassifier = new();
    private readonly RemoteChangeClassifier _remoteClassifier = new();

    public SyncEngine(JsonSyncMetadataStore store, CloudApiService api)
    {
        _store = store;
        _api = api;
    }

    public Task ReconcileAllAsync(
        IReadOnlyList<SyncMappingInfo> mappings,
        CancellationToken ct = default) =>
        _store.RunInBatchAsync(() => ReconcileAllCoreAsync(mappings, ct));

    private async Task ReconcileAllCoreAsync(
        IReadOnlyList<SyncMappingInfo> mappings,
        CancellationToken ct)
    {
        foreach (var mapping in mappings)
        {
            ct.ThrowIfCancellationRequested();
            var meta = _store.GetAll(mapping.Id);
            // Empty / folders-only / near-empty file inventory (partial Force or interrupted
            // bootstrap left folders + few file rows) → seed remote inventory before Compare.
            // Inventory-only (no getFile): ApplyRemote materializes missing locals; cancel after
            // checkpoints must leave file rows on disk (1.0.120 live FAIL under AutoPushBudget).
            if (SyncMetaInventoryHeal.NeedsRemoteBootstrap(meta, mapping.LocalRootPath))
                await SeedInventoryFromRemoteAsync(mapping, ct);
        }

        var remoteLocated = new List<RemoteLocated>();
        // Walk all mappings first so ApplyRemote can see cross-mapping renames.
        foreach (var mapping in mappings)
            await WalkRemoteAsync(mapping.CloudFolderId, mapping.Id, "", remoteLocated, ct);

        var byId = mappings.ToDictionary(m => m.Id);

        // Always hash-compare every mapped file so all devices converge to the same bytes.
        await EnrichRemoteSizesAsync(remoteLocated, ct);
        // Per-mapping Compare→Remote→Local→LastSync: do not wait for sibling maps' Apply*
        // before stamping (SynchronizeAll across ~15 roots left MyNotesFolder LastSync idle
        // past 15m on 1.0.76 after values-light WalkRemote).
        foreach (var mapping in mappings)
        {
            ct.ThrowIfCancellationRequested();
            var dirty = new HashSet<string>(StringComparer.Ordinal);
            await ApplyComparePhaseAsync(mapping, remoteLocated, dirty, ct);
            // Cloud materialize before local upload so polluted Notes trees (local junk) cannot
            // starve / timeout the pass before missing remote children are filled (cloud-wins).
            await ApplyRemotePhaseAsync(mapping, byId, remoteLocated, dirty, ct);
            var remoteForMapping = remoteLocated.Where(x => x.MappingId == mapping.Id).ToList();
            // Same Agents exclude as AutoPush so full Sync settles under mapped MyNotesFolder
            // churn (1.0.119 sticky Syncing… after Autopush release + collapsed meta).
            await ApplyLocalPhaseAsync(
                mapping,
                dirty,
                skipSizeUpdates: true,
                remoteForMapping,
                ct,
                excludeAgentsQueues: true);
            _store.SetLastSyncUtc(mapping.Id, DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Upload local FS changes vs meta only (no remote walk / download).
    /// Mappings with empty metadata are fully reconciled first (never blind-create remote entities).
    /// Returns number of local structural/content ops applied from pure push passes.
    /// </summary>
    public Task<int> PushLocalChangesAsync(
        IReadOnlyList<SyncMappingInfo> mappings,
        CancellationToken ct = default) =>
        PushLocalChangesBatchedAsync(mappings, ct);

    private async Task<int> PushLocalChangesBatchedAsync(
        IReadOnlyList<SyncMappingInfo> mappings,
        CancellationToken ct)
    {
        var applied = 0;
        var needFull = new List<SyncMappingInfo>();
        var ready = new List<SyncMappingInfo>();
        foreach (var mapping in mappings)
        {
            if (!Directory.Exists(mapping.LocalRootPath)) continue;
            var meta = _store.GetAll(mapping.Id);
            // Empty / folders-only / near-empty file inventory → full reconcile before push.
            if (SyncMetaInventoryHeal.NeedsRemoteBootstrap(meta, mapping.LocalRootPath))
                needFull.Add(mapping);
            else
                ready.Add(mapping);
        }

        if (needFull.Count > 0)
            await ReconcileAllAsync(needFull, ct);

        await _store.RunInBatchAsync(async () =>
        {
            foreach (var mapping in ready)
            {
                var started = false;
                try
                {
                    ct.ThrowIfCancellationRequested();
                    started = true;
                    var dirty = new HashSet<string>(StringComparer.Ordinal);
                    // Exclude Agents role/mailbox queues so Autopush settles under mapped
                    // MyNotesFolder while AgentTeam rewrites task HTML (1.0.118 churn class).
                    applied += await ApplyLocalPhaseAsync(
                        mapping,
                        dirty,
                        skipSizeUpdates: false,
                        remoteInMapping: new List<RemoteLocated>(),
                        ct,
                        excludeAgentsQueues: true);
                    _store.SetLastSyncUtc(mapping.Id, DateTime.UtcNow);
                }
                catch (OperationCanceledException)
                {
                    // Budget/preempt: still stamp LastSync once this mapping's push started so
                    // a 60s AutoPush cancel cannot leave the live map without a LastSync key.
                    if (started)
                        _store.SetLastSyncUtc(mapping.Id, DateTime.UtcNow);
                    throw;
                }
            }
        });
        return applied;
    }

    /// <summary>
    /// Per-item getFile budget. HttpClient.Timeout alone did not abort a live blackholed getFile
    /// (1.0.77 dump: ApplyCompare stuck &gt;35m with Timeout=100s) — link CancelAfter so one hung
    /// download cannot keep the meta batch open past the daily-use LastSync window.
    /// </summary>
    /// <summary>Test hook may shorten; production default 45s.</summary>
    internal static TimeSpan RemoteDownloadBudget { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>Buffer a remote file in memory so the disk write stays short and ungated during transfer.</summary>
    private async Task<byte[]?> DownloadBytesOrNullAsync(Guid remoteId, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RemoteDownloadBudget);
        try
        {
            await using var stream = await _api.GetFileStreamOrNotFoundAsync(remoteId, timeoutCts.Token);
            if (stream == null) return null;
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, timeoutCts.Token);
            return ms.ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Per-item timeout / transport cancel — soft-fail like null download (NFR-SYNC-1 adjacent).
            // Do not let HttpClient TaskCanceledException abort the whole reconcile before LastSync.
            SyncRemoteDownload.LogFailed("DownloadBytesOrNullAsync.Timeout", remoteId.ToString("D"), remoteId);
            return null;
        }
    }

    private Task EnrichRemoteSizesAsync(List<RemoteLocated> located, CancellationToken ct)
    {
        // OpenResty serves getFile as chunked (no Content-Length). Header-only probes still
        // open a body stream per file and stalled mapped sync for minutes, so Films never
        // uploaded. Decide uses UpdateTime + ContentHash (unseeded remote hash probe) instead.
        _ = located;
        _ = ct;
        return Task.CompletedTask;
    }

    private async Task ApplyComparePhaseAsync(
        SyncMappingInfo mapping,
        List<RemoteLocated> allRemote,
        HashSet<string> dirty,
        CancellationToken ct)
    {
        // Do not SnapshotFs here — compare hashes per meta row only. A full-tree snapshot
        // (esp. legacy O(dirs×files) folder sizes) starved LastSync on ~10k MyNotesFolder.
        var remoteById = allRemote
            .GroupBy(r => r.Entry.Id)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var meta in _store.GetAll(mapping.Id))
        {
            ct.ThrowIfCancellationRequested();
            if (meta.IsFolder) continue;
            if (!remoteById.TryGetValue(meta.RemoteId, out var located)) continue;
            if (located.MappingId != mapping.Id) continue;
            if (SyncPathUtil.Normalize(located.RelativePath) != SyncPathUtil.Normalize(meta.RelativePath))
                continue;

            var path = SyncPathUtil.Normalize(meta.RelativePath);
            var full = ToFull(mapping.LocalRootPath, meta.RelativePath);
            long? localSize = File.Exists(full) ? new FileInfo(full).Length : null;

            // Missing local while meta exists = delete/rename — structural phase handles it.
            if (localSize == null) continue;

            // Empty / tiny stubs: prefer remote when cloud likely has real content.
            if (localSize == 0L || (localSize < 64L && (located.Entry.SizeBytes ?? 0L) > localSize))
            {
                var stubBytes = await DownloadBytesOrNullAsync(meta.RemoteId, ct);
                if (stubBytes is { Length: > 0 })
                {
                    await MappedFolderIoGate.WriteAllBytesAsync(full, stubBytes, ct);
                    meta.SizeBytes = stubBytes.Length;
                    meta.ContentHash = ContentHashUtil.Sha256Hex(stubBytes);
                    meta.RemoteUpdateTime = located.Entry.UpdateTime ?? DateTime.UtcNow;
                    _store.Upsert(meta);
                    dirty.Add(path);
                }
                continue;
            }

            // Size+baseline+listing fast path: avoid SHA-256 of every unchanged MyNotesFolder file
            // before getFile skip (1.0.75 quiet-meta hang class after WalkRemote/DevTools).
            if (ContentHashUtil.CanSkipCompareWithoutHash(
                    localSize.Value,
                    meta.SizeBytes,
                    meta.ContentHash,
                    located.Entry.UpdateTime,
                    meta.RemoteUpdateTime))
                continue;

            string localHash;
            try
            {
                var hashed = ContentHashUtil.TrySha256HexFile(full);
                if (hashed == null)
                {
                    SyncMissingLocalPath.Log("ApplyComparePhase", path, full);
                    continue;
                }
                localHash = hashed;
            }
            catch (Exception ex) when (SyncMissingLocalPath.IsMissing(ex))
            {
                SyncMissingLocalPath.Log("ApplyComparePhase", path, full, ex);
                continue;
            }

            // Unchanged local + remote listing not newer → skip getFile (bounded reconcile on
            // large MyNotesFolder trees after autopush preempt). Still download when local
            // diverged from meta or remote UpdateTime is newer — never forge remoteHash.
            if (ContentHashUtil.CanSkipRemoteContentDownload(
                    localHash,
                    meta.ContentHash,
                    located.Entry.UpdateTime,
                    meta.RemoteUpdateTime))
                continue;

            // null = failed/not-found → soft-fail this item; Length==0 is legitimate empty content.
            var remoteBytes = await DownloadBytesOrNullAsync(meta.RemoteId, ct);
            if (remoteBytes == null)
            {
                SyncRemoteDownload.LogFailed("ApplyComparePhase", path, meta.RemoteId);
                continue;
            }

            var remoteHash = ContentHashUtil.Sha256Hex(remoteBytes);
            try
            {
                switch (ContentHashUtil.DecideByHash(
                    localHash,
                    remoteHash,
                    meta.ContentHash,
                    located.Entry.UpdateTime,
                    meta.RemoteUpdateTime))
                {
                    case SyncDirectionDecide.Action.Write:
                    {
                        var name = SyncPathUtil.NameOf(meta.RelativePath);
                        var len = new FileInfo(full).Length;
                        await using var content = await MappedFolderIoGate.OpenUploadStreamAsync(full, ct);
                        await _api.AddFileAsync(meta.RemoteId, name, content, MimeTypes.GetMimeType(name));
                        meta.SizeBytes = len;
                        meta.ContentHash = localHash;
                        // Prefer listing time after upload is unknown; stamp now then next sync verifies.
                        meta.RemoteUpdateTime = DateTime.UtcNow;
                        _store.Upsert(meta);
                        dirty.Add(path);
                        break;
                    }
                    case SyncDirectionDecide.Action.Read:
                    {
                        await MappedFolderIoGate.WriteAllBytesAsync(full, remoteBytes, ct);
                        meta.SizeBytes = remoteBytes.Length;
                        meta.ContentHash = remoteHash;
                        meta.RemoteUpdateTime = located.Entry.UpdateTime ?? DateTime.UtcNow;
                        _store.Upsert(meta);
                        dirty.Add(path);
                        break;
                    }
                    case SyncDirectionDecide.Action.Skip:
                    {
                        meta.ContentHash = localHash;
                        if (located.Entry.UpdateTime is DateTime listed)
                            meta.RemoteUpdateTime = listed;
                        _store.Upsert(meta);
                        break;
                    }
                }
            }
            catch (Exception ex) when (SyncMissingLocalPath.IsMissing(ex))
            {
                SyncMissingLocalPath.Log("ApplyComparePhase", path, full, ex);
            }
        }
    }

    /// <summary>
    /// Rebuild sync-meta folders + file rows from remote listing without getFile.
    /// Existing locals are hashed in place; missing remotes stay out of meta so ApplyRemote
    /// can still classify them as Added. Progressive checkpoints survive budget cancel.
    /// </summary>
    private async Task SeedInventoryFromRemoteAsync(SyncMappingInfo mapping, CancellationToken ct)
    {
        var located = new List<RemoteLocated>();
        var checkpointCounter = new int[1];
        await WalkRemoteAsync(
            mapping.CloudFolderId,
            mapping.Id,
            "",
            located,
            ct,
            onFolderListed: batch =>
            {
                SeedDiscoveredInventory(mapping, batch);
                // Parallel WalkRemote folders — Interlocked so cancel still sees disk progress.
                if (Interlocked.Increment(ref checkpointCounter[0]) % 8 == 0)
                    _store.FlushBatchCheckpoint();
            });
        // Final inventory persist before Compare/Apply* (or cancel) so disk leaves folders-nearly-only.
        _store.FlushBatchCheckpoint();
        RecomputeFolderSizes(mapping);
        _store.FlushBatchCheckpoint();
    }

    private void SeedDiscoveredInventory(SyncMappingInfo mapping, IReadOnlyList<RemoteLocated> batch)
    {
        foreach (var loc in batch)
        {
            var full = Path.Combine(
                mapping.LocalRootPath,
                loc.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (loc.Entry.IsFolder)
            {
                if (DeletedLocalMirrorTombstones.IsUnderTombstone(full))
                    continue;
                try { Directory.CreateDirectory(full); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    SyncMissingLocalPath.Log("SeedInventory", loc.RelativePath, full, ex);
                }

                _store.Upsert(new SyncItemMeta
                {
                    MappingId = mapping.Id,
                    RemoteId = loc.Entry.Id,
                    ParentRemoteId = loc.Entry.ParentId,
                    RelativePath = loc.RelativePath,
                    IsFolder = true,
                    SizeBytes = 0,
                    RemoteUpdateTime = loc.Entry.UpdateTime
                });
                continue;
            }

            // Only seed file rows when local bytes exist — missing remotes stay for ApplyRemote.Added.
            if (!File.Exists(full))
                continue;

            long len;
            try { len = new FileInfo(full).Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SyncMissingLocalPath.Log("SeedInventory", loc.RelativePath, full, ex);
                continue;
            }

            var hashed = ContentHashUtil.TrySha256HexFile(full);
            if (hashed == null)
            {
                SyncMissingLocalPath.Log("SeedInventory", loc.RelativePath, full);
                continue;
            }

            _store.Upsert(new SyncItemMeta
            {
                MappingId = mapping.Id,
                RemoteId = loc.Entry.Id,
                ParentRemoteId = loc.Entry.ParentId,
                RelativePath = loc.RelativePath,
                IsFolder = false,
                SizeBytes = len,
                ContentHash = hashed,
                RemoteUpdateTime = loc.Entry.UpdateTime
            });
        }
    }

    private void RecomputeFolderSizes(SyncMappingInfo mapping)
    {
        var snap = SnapshotFs(mapping.LocalRootPath)
            .GroupBy(e => SyncPathUtil.Normalize(e.RelativePath), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var item in _store.GetAll(mapping.Id).Where(i => i.IsFolder))
        {
            if (snap.TryGetValue(SyncPathUtil.Normalize(item.RelativePath), out var fs) && fs.SizeBytes != item.SizeBytes)
            {
                item.SizeBytes = fs.SizeBytes;
                _store.Upsert(item);
            }
        }
    }

    private async Task<int> ApplyLocalPhaseAsync(
        SyncMappingInfo mapping,
        HashSet<string> dirty,
        bool skipSizeUpdates,
        List<RemoteLocated> remoteInMapping,
        CancellationToken ct,
        bool excludeAgentsQueues = false)
    {
        var applied = 0;
        // Reconcile: ApplyCompare already hash-verified content — size/structure snapshot only.
        // Push: need ContentHash so same-size local edits still classify as Updated.
        Func<string, bool>? skipPath = excludeAgentsQueues
            ? AgentsRoleQueuePaths.ShouldSkipLocalPush
            : null;
        var snap = SnapshotFs(
            mapping.LocalRootPath,
            computeContentHashes: !skipSizeUpdates,
            skipRelativePath: skipPath);
        var meta = _store.GetAll(mapping.Id);
        if (excludeAgentsQueues)
        {
            // Pair snap+meta filters so skipped Agents paths do not classify as Removed.
            meta = meta
                .Where(m => !AgentsRoleQueuePaths.ShouldSkipLocalPush(m.RelativePath))
                .ToList();
        }

        var changes = _localClassifier.Classify(snap, meta);
        // Cloud can list the same display name twice (or multi-parent graphs); keep newest.
        var remoteByPath = remoteInMapping
            .GroupBy(x => SyncPathUtil.Normalize(x.RelativePath), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.Entry.UpdateTime ?? DateTime.MinValue).First(),
                StringComparer.Ordinal);
        // Parent→children cache for match-before-create (push / walk miss / synthetic skip).
        var siblingCache = new Dictionary<Guid, List<RemoteEntryInfo>>();

        foreach (var r in changes.OfType<LocalChange.Renamed>())
        {
            ct.ThrowIfCancellationRequested();
            var newParentPath = SyncPathUtil.ParentOf(r.NewRelativePath);
            var newParentId = ResolveParentId(mapping, newParentPath) ?? mapping.CloudFolderId;
            if (newParentId != r.Meta.ParentRemoteId)
            {
                await _api.RemoveParentAsync(r.Meta.RemoteId, r.Meta.ParentRemoteId);
                await _api.AddParentAsync(r.Meta.RemoteId, newParentId);
            }
            if (!string.Equals(SyncPathUtil.NameOf(r.Meta.RelativePath), r.NewName, StringComparison.OrdinalIgnoreCase))
                await _api.AddValueAsync(r.Meta.RemoteId, r.NewName);
            _store.Delete(mapping.Id, r.Meta.RelativePath);
            _store.Upsert(new SyncItemMeta
            {
                MappingId = mapping.Id,
                RemoteId = r.Meta.RemoteId,
                ParentRemoteId = newParentId,
                RelativePath = r.NewRelativePath,
                IsFolder = r.Meta.IsFolder,
                SizeBytes = r.Meta.SizeBytes,
                ContentHash = r.Meta.ContentHash,
                RemoteUpdateTime = r.Meta.RemoteUpdateTime
            });
            dirty.Add(r.NewRelativePath);
            dirty.Add(SyncPathUtil.Normalize(r.Meta.RelativePath));
            applied++;
        }

        // Buffer same-pass Creates → one AddEntities when planting parent+children (listing lag).
        var pendingCreates = new List<PendingLocalCreate>();
        var pendingCreatesByPath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var pendingCreateIds = new HashSet<Guid>();

        foreach (var a in changes.OfType<LocalChange.Added>().OrderBy(x => x.Entry.RelativePath.Count(c => c == '/')))
        {
            ct.ThrowIfCancellationRequested();
            var path = SyncPathUtil.Normalize(a.Entry.RelativePath);
            var addFullProbe = ToFull(mapping.LocalRootPath, a.Entry.RelativePath);
            try
            {
                RemoteLocated? existingRemote = null;
                if (remoteByPath.TryGetValue(path, out var byPath))
                    existingRemote = byPath;
                else
                {
                    // Path miss (synthetic skip / push with empty walk): match by name under parent.
                    var parentPathProbe = SyncPathUtil.ParentOf(a.Entry.RelativePath);
                    var parentPathNorm = SyncPathUtil.Normalize(parentPathProbe);
                    var parentIdProbe = (pendingCreatesByPath.TryGetValue(parentPathNorm, out var pendingParentProbe)
                            ? pendingParentProbe
                            : ResolveParentId(mapping, parentPathProbe))
                        ?? mapping.CloudFolderId;
                    var nameProbe = SyncPathUtil.NameOf(a.Entry.RelativePath);
                    RemoteEntryInfo? byName;
                    bool listingOk;
                    if (pendingCreatesByPath.ContainsKey(parentPathNorm)
                        || pendingCreateIds.Contains(parentIdProbe))
                    {
                        listingOk = true;
                        byName = siblingCache.TryGetValue(parentIdProbe, out var kidsProbe)
                            ? SameNamedCloudSibling.PreferExisting(
                                parentIdProbe, nameProbe, a.Entry.IsFolder, kidsProbe)
                            : null;
                    }
                    else
                    {
                        (byName, listingOk) = await ResolveExistingSiblingAsync(
                            parentIdProbe, nameProbe, a.Entry.IsFolder, remoteInMapping, siblingCache, ct);
                    }
                    if (byName != null)
                    {
                        existingRemote = new RemoteLocated
                        {
                            Entry = byName,
                            MappingId = mapping.Id,
                            RelativePath = path
                        };
                    }
                    else if (!listingOk && remoteInMapping.Count == 0)
                    {
                        // Push with empty walk: cannot prove absence — soft-fail (do not mint sibling).
                        System.Diagnostics.Debug.WriteLine(
                            $"[Sync] skip create {path}: sibling listing failed under {parentIdProbe:D}");
                        continue;
                    }
                }

                if (existingRemote != null)
                {
                    var name = SyncPathUtil.NameOf(a.Entry.RelativePath);
                    var full = addFullProbe;
                    if (a.Entry.IsFolder)
                    {
                        _store.Upsert(new SyncItemMeta
                        {
                            MappingId = mapping.Id,
                            RemoteId = existingRemote.Entry.Id,
                            ParentRemoteId = existingRemote.Entry.ParentId,
                            RelativePath = path,
                            IsFolder = true,
                            SizeBytes = a.Entry.SizeBytes,
                            RemoteUpdateTime = existingRemote.Entry.UpdateTime
                        });
                    }
                    else
                    {
                        if (!File.Exists(full))
                        {
                            SyncMissingLocalPath.Log("ApplyLocalPhase.Added", path, full);
                            continue;
                        }
                        var len = new FileInfo(full).Length;
                        var remoteSize = existingRemote.Entry.SizeBytes;
                        var localLooksStub = len == 0 || (len < 64 && remoteSize is > 0 && remoteSize > len);
                        if (localLooksStub)
                        {
                            var cloudBytes = await DownloadBytesOrNullAsync(existingRemote.Entry.Id, ct);
                            if (cloudBytes != null)
                            {
                                await MappedFolderIoGate.WriteAllBytesAsync(full, cloudBytes, ct);
                                _store.Upsert(new SyncItemMeta
                                {
                                    MappingId = mapping.Id,
                                    RemoteId = existingRemote.Entry.Id,
                                    ParentRemoteId = existingRemote.Entry.ParentId,
                                    RelativePath = path,
                                    IsFolder = false,
                                    SizeBytes = cloudBytes.Length,
                                    ContentHash = ContentHashUtil.Sha256Hex(cloudBytes),
                                    RemoteUpdateTime = existingRemote.Entry.UpdateTime ?? DateTime.UtcNow
                                });
                                dirty.Add(path);
                                applied++;
                            }
                            continue;
                        }
                        // Adopt existing remote id: converge by content hash (never upload solely because size is unknown).
                        // null = failed/not-found → soft-fail this item; Length==0 is legitimate empty content.
                        var remoteBytes = await DownloadBytesOrNullAsync(existingRemote.Entry.Id, ct);
                        if (remoteBytes == null)
                        {
                            SyncRemoteDownload.LogFailed(
                                "ApplyLocalPhase.Added", path, existingRemote.Entry.Id);
                            continue;
                        }
                        var localHash = ContentHashUtil.TrySha256HexFile(full);
                        if (localHash == null)
                        {
                            SyncMissingLocalPath.Log("ApplyLocalPhase.Added", path, full);
                            continue;
                        }
                        var remoteHash = ContentHashUtil.Sha256Hex(remoteBytes);
                        var action = ContentHashUtil.DecideByHash(
                            localHash, remoteHash, metaHash: null,
                            existingRemote.Entry.UpdateTime, metaUpdateTime: null);
                        if (action == SyncDirectionDecide.Action.Write)
                        {
                            await using var content = await MappedFolderIoGate.OpenUploadStreamAsync(full, ct);
                            await _api.AddFileAsync(existingRemote.Entry.Id, name, content, MimeTypes.GetMimeType(name));
                            _store.Upsert(new SyncItemMeta
                            {
                                MappingId = mapping.Id,
                                RemoteId = existingRemote.Entry.Id,
                                ParentRemoteId = existingRemote.Entry.ParentId,
                                RelativePath = path,
                                IsFolder = false,
                                SizeBytes = len,
                                ContentHash = localHash,
                                RemoteUpdateTime = DateTime.UtcNow
                            });
                            applied++;
                        }
                        else
                        {
                            // Read / Skip → adopt cloud bytes when hashes differ (cloud-wins).
                            if (action != SyncDirectionDecide.Action.Skip)
                                await MappedFolderIoGate.WriteAllBytesAsync(full, remoteBytes, ct);
                            _store.Upsert(new SyncItemMeta
                            {
                                MappingId = mapping.Id,
                                RemoteId = existingRemote.Entry.Id,
                                ParentRemoteId = existingRemote.Entry.ParentId,
                                RelativePath = path,
                                IsFolder = false,
                                SizeBytes = remoteBytes.Length,
                                ContentHash = remoteHash,
                                RemoteUpdateTime = existingRemote.Entry.UpdateTime ?? DateTime.UtcNow
                            });
                            applied++;
                        }
                    }
                    dirty.Add(path);
                    continue;
                }
                var parentPath = SyncPathUtil.ParentOf(a.Entry.RelativePath);
                // Overlay: parent may be a same-pass buffered create (TempId not yet on server).
                var parentId = (pendingCreatesByPath.TryGetValue(SyncPathUtil.Normalize(parentPath), out var pendingParent)
                        ? pendingParent
                        : ResolveParentId(mapping, parentPath))
                    ?? mapping.CloudFolderId;
                var addName = SyncPathUtil.NameOf(a.Entry.RelativePath);
                var addFull = addFullProbe;
                // Final match-before-create (cache may have been filled by earlier Adds).
                // New parents in this pass: skip live GetEntities (id not on server yet) — siblingCache only.
                RemoteEntryInfo? reuse;
                bool listingOkCreate;
                if (pendingCreatesByPath.ContainsKey(SyncPathUtil.Normalize(parentPath))
                    || pendingCreateIds.Contains(parentId))
                {
                    listingOkCreate = true;
                    reuse = siblingCache.TryGetValue(parentId, out var kids)
                        ? SameNamedCloudSibling.PreferExisting(parentId, addName, a.Entry.IsFolder, kids)
                        : null;
                }
                else
                {
                    (reuse, listingOkCreate) = await ResolveExistingSiblingAsync(
                        parentId, addName, a.Entry.IsFolder, remoteInMapping, siblingCache, ct);
                }
                if (reuse == null && !listingOkCreate)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Sync] skip create {path}: sibling listing failed under {parentId:D}");
                    continue;
                }
                if (reuse != null)
                {
                    var id = reuse.Id;
                    if (!a.Entry.IsFolder)
                    {
                        if (!File.Exists(addFull))
                        {
                            SyncMissingLocalPath.Log("ApplyLocalPhase.Added", path, addFull);
                            continue;
                        }
                        if (new FileInfo(addFull).Length == 0) continue;
                        await using var content = await MappedFolderIoGate.OpenUploadStreamAsync(addFull, ct);
                        await _api.AddFileAsync(id, addName, content, MimeTypes.GetMimeType(addName));
                    }
                    string reuseHash = "";
                    if (!a.Entry.IsFolder)
                    {
                        if (a.Entry.ContentHash.Length > 0)
                            reuseHash = a.Entry.ContentHash;
                        else
                        {
                            reuseHash = ContentHashUtil.TrySha256HexFile(addFull) ?? "";
                            if (reuseHash.Length == 0)
                                SyncMissingLocalPath.Log("ApplyLocalPhase.Added", path, addFull);
                        }
                    }
                    _store.Upsert(new SyncItemMeta
                    {
                        MappingId = mapping.Id,
                        RemoteId = id,
                        ParentRemoteId = parentId,
                        RelativePath = SyncPathUtil.Normalize(a.Entry.RelativePath),
                        IsFolder = a.Entry.IsFolder,
                        SizeBytes = a.Entry.SizeBytes,
                        ContentHash = reuseHash,
                        RemoteUpdateTime = DateTime.UtcNow
                    });
                    dirty.Add(SyncPathUtil.Normalize(a.Entry.RelativePath));
                    applied++;
                    continue;
                }

                if (!a.Entry.IsFolder)
                {
                    if (!File.Exists(addFull))
                    {
                        SyncMissingLocalPath.Log("ApplyLocalPhase.Added", path, addFull);
                        continue;
                    }
                    if (new FileInfo(addFull).Length == 0) continue;
                }

                // Buffer new entities — flush via AddEntities (≥2) or AddEntity (1) after the pass.
                var tempId = Guid.NewGuid();
                var code = a.Entry.IsFolder ? CloudEntityCodes.CloudFolder : CloudEntityCodes.CloudFile;
                pendingCreates.Add(new PendingLocalCreate(
                    tempId, code, parentId, addName, path, addFull, a.Entry.IsFolder, a.Entry.SizeBytes,
                    a.Entry.ContentHash));
                pendingCreatesByPath[path] = tempId;
                pendingCreateIds.Add(tempId);
                RememberSibling(siblingCache, parentId, tempId, addName, isFolder: a.Entry.IsFolder);
            }
            catch (Exception ex) when (SyncMissingLocalPath.IsMissing(ex))
            {
                SyncMissingLocalPath.Log("ApplyLocalPhase.Added", path, addFullProbe, ex);
            }
        }

        if (pendingCreates.Count > 0)
        {
            applied += await FlushPendingLocalCreatesAsync(mapping, pendingCreates, ct);
            foreach (var p in pendingCreates)
                dirty.Add(p.RelativePath);
        }

        foreach (var u in changes.OfType<LocalChange.Updated>())
        {
            ct.ThrowIfCancellationRequested();
            var updatedPath = SyncPathUtil.Normalize(u.Meta.RelativePath);
            var full = ToFull(mapping.LocalRootPath, u.Meta.RelativePath);
            try
            {
                if (u.Meta.IsFolder)
                {
                    u.Meta.SizeBytes = u.NewSize;
                    _store.Upsert(u.Meta);
                    dirty.Add(updatedPath);
                    continue;
                }
                if (skipSizeUpdates)
                {
                    // Content convergence is owned by ApplyComparePhase (always hash-verify).
                    // Do not upload here — that overwrote unknown cloud content when download failed.
                    u.Meta.SizeBytes = u.NewSize;
                    _store.Upsert(u.Meta);
                    continue;
                }
                if (!File.Exists(full))
                {
                    SyncMissingLocalPath.Log("ApplyLocalPhase.Updated", updatedPath, full);
                    continue;
                }
                var name = SyncPathUtil.NameOf(u.Meta.RelativePath);
                await using var content2 = await MappedFolderIoGate.OpenUploadStreamAsync(full, ct);
                await _api.AddFileAsync(u.Meta.RemoteId, name, content2, MimeTypes.GetMimeType(name));
                u.Meta.SizeBytes = u.NewSize;
                var hashed = ContentHashUtil.TrySha256HexFile(full);
                if (hashed == null)
                    SyncMissingLocalPath.Log("ApplyLocalPhase.Updated", updatedPath, full);
                else
                    u.Meta.ContentHash = hashed;
                u.Meta.RemoteUpdateTime = DateTime.UtcNow;
                _store.Upsert(u.Meta);
                dirty.Add(updatedPath);
                applied++;
            }
            catch (Exception ex) when (SyncMissingLocalPath.IsMissing(ex))
            {
                SyncMissingLocalPath.Log("ApplyLocalPhase.Updated", updatedPath, full, ex);
            }
        }

        // Deepest-first so child meta rows usually go first; folder Removed uses single-root
        // DeleteEntity (Api cascade) + local sync-meta ids for alreadyDeleted tracking.
        var alreadyDeleted = new HashSet<Guid>();
        foreach (var rm in changes.OfType<LocalChange.Removed>().OrderByDescending(x => x.Meta.RelativePath.Count(c => c == '/')))
        {
            ct.ThrowIfCancellationRequested();
            if (!alreadyDeleted.Add(rm.Meta.RemoteId))
            {
                _store.Delete(mapping.Id, rm.Meta.RelativePath);
                dirty.Add(SyncPathUtil.Normalize(rm.Meta.RelativePath));
                continue;
            }

            if (rm.Meta.IsFolder)
            {
                // Known local sync-meta descendants (no GetEntities walk) + one DeleteEntity —
                // Api cascade owns the Cloud subtree (prod ≥1.0.92). Avoid N+1 re-cascade.
                var cascadeDeleted = new HashSet<Guid> { rm.Meta.RemoteId };
                var prefix = SyncPathUtil.Normalize(rm.Meta.RelativePath ?? "").TrimEnd('/');
                if (!string.IsNullOrEmpty(prefix))
                {
                    foreach (var row in _store.GetAll(mapping.Id))
                    {
                        if (row.RemoteId == Guid.Empty) continue;
                        var path = SyncPathUtil.Normalize(row.RelativePath ?? "");
                        if (path == prefix || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                            cascadeDeleted.Add(row.RemoteId);
                    }
                }

                await CloudFolderDelete.DeleteSingleRootAsync(
                    rm.Meta.RemoteId,
                    id => _api.DeleteEntityAsync(id),
                    cascadeDeleted).ConfigureAwait(false);
                foreach (var id in cascadeDeleted)
                    alreadyDeleted.Add(id);
            }
            else
            {
                await _api.DeleteEntityAsync(rm.Meta.RemoteId).ConfigureAwait(false);
            }

            _store.Delete(mapping.Id, rm.Meta.RelativePath);
            dirty.Add(SyncPathUtil.Normalize(rm.Meta.RelativePath));
            applied++;
        }

        return applied;
    }

    private async Task ApplyRemotePhaseAsync(
        SyncMappingInfo mapping,
        Dictionary<string, SyncMappingInfo> mappingsById,
        List<RemoteLocated> allRemote,
        HashSet<string> dirty,
        CancellationToken ct)
    {
        var meta = _store.GetAll(mapping.Id);
        var allIds = _store.GetAllRemoteIds();
        // Do not probe size via GetFileStream: HTTP streams often report Length 0/-1, which made
        // every file look "updated" and re-triggered sync forever. Leave SizeBytes null unless
        // the listing provided it; classifier then relies on UpdateTime / presence only.
        var cross = allRemote;
        var changes = _remoteClassifier.Classify(meta, cross, allIds, dirty);

        foreach (var c in changes)
        {
            ct.ThrowIfCancellationRequested();
            switch (c)
            {
                case RemoteChange.Renamed ren:
                {
                    if (ren.Meta.MappingId != mapping.Id) break;
                    var located = cross.FirstOrDefault(x => x.Entry.Id == ren.Meta.RemoteId);
                    if (located != null && located.MappingId != mapping.Id)
                    {
                        var dest = mappingsById[located.MappingId];
                        var fromFull = ToFull(mapping.LocalRootPath, ren.Meta.RelativePath);
                        var toFull = ToFull(dest.LocalRootPath, located.RelativePath);
                        if (ren.Meta.IsFolder)
                        {
                            if (Directory.Exists(fromFull))
                                await MappedFolderIoGate.MoveDirectoryAsync(fromFull, toFull);
                            else
                                Directory.CreateDirectory(toFull);
                        }
                        else if (File.Exists(fromFull))
                        {
                            await MappedFolderIoGate.MoveFileAsync(fromFull, toFull);
                        }
                        else
                        {
                            var moved = await DownloadBytesOrNullAsync(ren.Meta.RemoteId, ct);
                            if (moved != null)
                                await MappedFolderIoGate.WriteAllBytesAsync(toFull, moved, ct);
                        }
                        _store.Delete(mapping.Id, ren.Meta.RelativePath);
                        _store.Upsert(new SyncItemMeta
                        {
                            MappingId = located.MappingId,
                            RemoteId = ren.Meta.RemoteId,
                            ParentRemoteId = located.Entry.ParentId,
                            RelativePath = SyncPathUtil.Normalize(located.RelativePath),
                            IsFolder = ren.Meta.IsFolder,
                            SizeBytes = located.Entry.SizeBytes ?? ren.Meta.SizeBytes,
                            RemoteUpdateTime = located.Entry.UpdateTime
                        });
                    }
                    else
                    {
                        var fromFull = ToFull(mapping.LocalRootPath, ren.Meta.RelativePath);
                        var toFull = ToFull(mapping.LocalRootPath, ren.NewRelativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(toFull)!);
                        if (ren.Meta.IsFolder && Directory.Exists(fromFull))
                            await MappedFolderIoGate.MoveDirectoryAsync(fromFull, toFull);
                        else if (!ren.Meta.IsFolder && File.Exists(fromFull))
                            await MappedFolderIoGate.MoveFileAsync(fromFull, toFull);
                        _store.Delete(mapping.Id, ren.Meta.RelativePath);
                        _store.Upsert(new SyncItemMeta
                        {
                            MappingId = mapping.Id,
                            RemoteId = ren.Meta.RemoteId,
                            ParentRemoteId = ren.Remote.ParentId,
                            RelativePath = ren.NewRelativePath,
                            IsFolder = ren.Meta.IsFolder,
                            SizeBytes = ren.Remote.SizeBytes ?? ren.Meta.SizeBytes,
                            RemoteUpdateTime = ren.Remote.UpdateTime
                        });
                    }
                    break;
                }
                case RemoteChange.Added add:
                {
                    if (!cross.Any(e => e.MappingId == mapping.Id && e.Entry.Id == add.Remote.Id)) break;
                    var full = ToFull(mapping.LocalRootPath, add.RelativePath);
                    if (DeletedLocalMirrorTombstones.IsUnderTombstone(full)) break;
                    try
                    {
                        if (add.Remote.IsFolder)
                        {
                            Directory.CreateDirectory(full);
                            _store.Upsert(new SyncItemMeta
                            {
                                MappingId = mapping.Id,
                                RemoteId = add.Remote.Id,
                                ParentRemoteId = add.Remote.ParentId,
                                RelativePath = add.RelativePath,
                                IsFolder = true,
                                SizeBytes = 0,
                                RemoteUpdateTime = add.Remote.UpdateTime
                            });
                        }
                        else
                        {
                            var addBytes = await DownloadBytesOrNullAsync(add.Remote.Id, ct) ?? Array.Empty<byte>();
                            await MappedFolderIoGate.WriteAllBytesAsync(full, addBytes, ct);
                            _store.Upsert(new SyncItemMeta
                            {
                                MappingId = mapping.Id,
                                RemoteId = add.Remote.Id,
                                ParentRemoteId = add.Remote.ParentId,
                                RelativePath = add.RelativePath,
                                IsFolder = false,
                                SizeBytes = addBytes.Length,
                                ContentHash = addBytes.Length > 0 ? ContentHashUtil.Sha256Hex(addBytes) : "",
                                RemoteUpdateTime = add.Remote.UpdateTime
                            });
                        }
                    }
                    catch (Exception ex) when (SyncMissingLocalPath.IsMissing(ex) || ex is IOException or UnauthorizedAccessException)
                    {
                        // NFR-SYNC-1: soft-fail per item; continue siblings.
                        SyncRemoteDownload.LogMaterializeFailed(
                            "ApplyRemotePhase.Added",
                            add.RelativePath,
                            add.Remote.Id.ToString("D"),
                            ex.Message);
                    }
                    break;
                }
                case RemoteChange.Removed rem:
                {
                    if (rem.Meta.MappingId != mapping.Id) break;
                    if (cross.Any(x => x.Entry.Id == rem.Meta.RemoteId)) break;
                    var full = ToFull(mapping.LocalRootPath, rem.Meta.RelativePath);
                    if (rem.Meta.IsFolder) await MappedFolderIoGate.DeleteDirectoryAsync(full);
                    else await MappedFolderIoGate.DeleteFileAsync(full);
                    _store.Delete(mapping.Id, rem.Meta.RelativePath);
                    break;
                }
                case RemoteChange.Updated upd:
                {
                    // Size/time content sync handled in ApplyComparePhase when enabled.
                    if (upd.Meta.MappingId != mapping.Id || upd.Meta.IsFolder) break;
                    if (dirty.Contains(SyncPathUtil.Normalize(upd.Meta.RelativePath))) break;
                    var full = ToFull(mapping.LocalRootPath, upd.Meta.RelativePath);
                    if (File.Exists(full) && new FileInfo(full).Length != upd.Meta.SizeBytes) break;
                    var updBytes = await DownloadBytesOrNullAsync(upd.Remote.Id, ct);
                    if (updBytes == null) break;
                    await MappedFolderIoGate.WriteAllBytesAsync(full, updBytes, ct);
                    _store.Upsert(new SyncItemMeta
                    {
                        MappingId = mapping.Id,
                        RemoteId = upd.Meta.RemoteId,
                        ParentRemoteId = upd.Meta.ParentRemoteId,
                        RelativePath = upd.Meta.RelativePath,
                        IsFolder = false,
                        SizeBytes = updBytes.Length,
                        ContentHash = ContentHashUtil.Sha256Hex(updBytes),
                        RemoteUpdateTime = upd.Remote.UpdateTime ?? DateTime.UtcNow
                    });
                    break;
                }
            }
        }
    }

    private async Task WalkRemoteAsync(
        Guid folderId,
        string mappingId,
        string relativeParent,
        List<RemoteLocated> outList,
        CancellationToken ct,
        HashSet<Guid>? visitedIds = null,
        HashSet<string>? seenPaths = null,
        Action<IReadOnlyList<RemoteLocated>>? onFolderListed = null)
    {
        visitedIds ??= new HashSet<Guid>();
        seenPaths ??= new HashSet<string>(StringComparer.Ordinal);
        // Parallel folder listings: MyNotesFolder has ~3.5k folders; concurrency 2 + DevTools
        // body buffering left WalkRemote outside the 15m LastSync budget (idle Syncing).
        // Keep aligned with GetEntitiesThrottlingHandler.SyncLaneConcurrency.
        // Warm meta: list without Values and reuse RelativePath names — IncludeValues×3.5k
        // still burned the 15m budget on 1.0.75 after DevTools omit + concurrency-8.
        var metaByRemoteId = _store.GetAll(mappingId)
            .GroupBy(m => m.RemoteId)
            .ToDictionary(g => g.Key, g => g.First());
        var gate = new object();
        using var throttle = new SemaphoreSlim(GetEntitiesThrottlingHandler.SyncLaneConcurrency);
        await WalkRemoteParallelAsync(
            folderId, mappingId, relativeParent, outList, ct, visitedIds, seenPaths, gate, throttle,
            metaByRemoteId, onFolderListed);
    }

    private async Task WalkRemoteParallelAsync(
        Guid folderId,
        string mappingId,
        string relativeParent,
        List<RemoteLocated> outList,
        CancellationToken ct,
        HashSet<Guid> visitedIds,
        HashSet<string> seenPaths,
        object gate,
        SemaphoreSlim throttle,
        IReadOnlyDictionary<Guid, SyncItemMeta> metaByRemoteId,
        Action<IReadOnlyList<RemoteLocated>>? onFolderListed = null)
    {
        // Cold bootstrap (empty meta): IncludeValues listing per folder.
        // Warm reconcile: structure-only first; if every CloudFile/Folder child is already in meta,
        // reuse RelativePath names (MyNotesFolder ~3.5k folders stay cheap). Any unknown child →
        // one IncludeValues re-list for that folder only (remote adds / heal paths).
        var warm = metaByRemoteId.Count > 0;
        await throttle.WaitAsync(ct);
        List<GetEntitiesResult> children;
        try
        {
            children = await _api.GetEntitiesAsync(new[] { folderId }, includeValues: !warm);
        }
        finally
        {
            throttle.Release();
        }

        var useMetaNames = false;
        if (warm)
        {
            var cloudKids = children
                .Where(c => CloudEntityClassify.IsCloudFolderOrFile(c.TypeEntityId, c.Code))
                .ToList();
            var allKnown = cloudKids.Count > 0 && cloudKids.All(c => metaByRemoteId.ContainsKey(c.Id));
            // Empty folder listing is fine with meta-name mode (nothing to name).
            if (cloudKids.Count == 0 || allKnown)
            {
                useMetaNames = true;
            }
            else
            {
                await throttle.WaitAsync(ct);
                try
                {
                    children = await _api.GetEntitiesAsync(new[] { folderId }, includeValues: true);
                }
                finally
                {
                    throttle.Release();
                }
            }
        }

        var subfolders = new List<(Guid Id, string Rel)>();
        List<RemoteLocated>? listedHere = onFolderListed == null ? null : new List<RemoteLocated>();
        lock (gate)
        {
            foreach (var child in children)
            {
                ct.ThrowIfCancellationRequested();
                var isFolder = CloudEntityClassify.IsFolder(child.TypeEntityId, child.Code);
                var isFile = CloudEntityClassify.IsFile(child.TypeEntityId, child.Code);
                if (!isFolder && !isFile) continue;
                if (!visitedIds.Add(child.Id)) continue;

                string? name;
                if (useMetaNames && metaByRemoteId.TryGetValue(child.Id, out var known))
                {
                    name = SyncPathUtil.NameOf(known.RelativePath);
                }
                else
                {
                    name = CloudApiService.GetDisplayName(child);
                    if (string.IsNullOrEmpty(name) || CloudApiService.IsSyntheticCloudFileDisplayName(child, name))
                        continue;
                }

                if (string.IsNullOrEmpty(name)) continue;
                var rel = SyncPathUtil.Join(relativeParent, name);
                if (!seenPaths.Add(SyncPathUtil.Normalize(rel))) continue;
                // Warm values-light (useMetaNames): structure-only GetEntities UpdateTime must not
                // drive ApplyCompare getFile. Live 1.0.77: listing times newer than meta for the
                // warm MyNotesFolder tree → getFile×~10k inside an open batch → LastSync never
                // stamped (quiet-meta sticky Syncing; dump hung in DownloadBytesOrNullAsync).
                // Trust meta baseline; IncludeValues re-list (unknown children) keeps UpdateTime.
                DateTime? listingUpdate = null;
                if (!useMetaNames && child.UpdateTime != default)
                    listingUpdate = child.UpdateTime;
                var entry = new RemoteEntryInfo
                {
                    Id = child.Id,
                    ParentId = folderId,
                    Name = name,
                    IsFolder = isFolder,
                    UpdateTime = listingUpdate
                };
                var located = new RemoteLocated { Entry = entry, MappingId = mappingId, RelativePath = rel };
                outList.Add(located);
                listedHere?.Add(located);
                if (isFolder)
                    subfolders.Add((child.Id, rel));
            }
        }

        if (listedHere is { Count: > 0 })
            onFolderListed!(listedHere);

        if (subfolders.Count == 0) return;
        await Task.WhenAll(subfolders.Select(sf => WalkRemoteParallelAsync(
            sf.Id, mappingId, sf.Rel, outList, ct, visitedIds, seenPaths, gate, throttle, metaByRemoteId,
            onFolderListed)));
    }

    private Guid? ResolveParentId(SyncMappingInfo mapping, string parentPath)
    {
        if (string.IsNullOrEmpty(parentPath)) return mapping.CloudFolderId;
        return _store.GetAll(mapping.Id)
            .FirstOrDefault(i => SyncPathUtil.Normalize(i.RelativePath) == SyncPathUtil.Normalize(parentPath))
            ?.RemoteId;
    }

    /// <summary>
    /// Match-before-create: walk list first, then live parent GetEntities (cached).
    /// Match null + ListingOk false → soft-fail (do not create a possible duplicate).
    /// Match null + ListingOk true → truly absent under parent.
    /// </summary>
    private async Task<(RemoteEntryInfo? Match, bool ListingOk)> ResolveExistingSiblingAsync(
        Guid parentId,
        string displayName,
        bool isFolder,
        List<RemoteLocated> remoteInMapping,
        Dictionary<Guid, List<RemoteEntryInfo>> siblingCache,
        CancellationToken ct)
    {
        var fromWalk = SameNamedCloudSibling.PreferExisting(
            parentId, displayName, isFolder, remoteInMapping.Select(r => r.Entry));
        if (fromWalk != null) return (fromWalk, true);

        if (!siblingCache.TryGetValue(parentId, out var kids))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var entities = await _api.GetEntitiesAsync(new[] { parentId }, includeValues: true);
                kids = new List<RemoteEntryInfo>();
                foreach (var e in entities)
                {
                    var isFolderCode = CloudEntityClassify.IsFolder(e.TypeEntityId, e.Code);
                    var isFileCode = CloudEntityClassify.IsFile(e.TypeEntityId, e.Code);
                    if (!isFolderCode && !isFileCode) continue;
                    var name = CloudApiService.GetDisplayName(e);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (isFileCode && CloudApiService.IsSyntheticCloudFileDisplayName(e, name))
                        continue;
                    kids.Add(new RemoteEntryInfo
                    {
                        Id = e.Id,
                        ParentId = parentId,
                        Name = name,
                        IsFolder = isFolderCode,
                        UpdateTime = e.UpdateTime == default ? null : e.UpdateTime
                    });
                }
                siblingCache[parentId] = kids;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Sync] sibling list failed parent={parentId:D}: {ex.Message}");
                return (null, false);
            }
        }

        return (SameNamedCloudSibling.PreferExisting(parentId, displayName, isFolder, kids), true);
    }

    private static void RememberSibling(
        Dictionary<Guid, List<RemoteEntryInfo>> siblingCache,
        Guid parentId,
        Guid id,
        string name,
        bool isFolder)
    {
        if (!siblingCache.TryGetValue(parentId, out var kids))
        {
            kids = new List<RemoteEntryInfo>();
            siblingCache[parentId] = kids;
        }
        kids.Add(new RemoteEntryInfo
        {
            Id = id,
            ParentId = parentId,
            Name = name,
            IsFolder = isFolder,
            UpdateTime = DateTime.UtcNow
        });
    }

    private sealed record PendingLocalCreate(
        Guid TempId,
        string Code,
        Guid ParentId,
        string Name,
        string RelativePath,
        string FullPath,
        bool IsFolder,
        long SizeBytes,
        string ContentHash);

    /// <summary>
    /// Plant buffered Creates: AddEntities (≥2 parent+children) or AddEntity (single), then file blobs.
    /// </summary>
    private async Task<int> FlushPendingLocalCreatesAsync(
        SyncMappingInfo mapping,
        List<PendingLocalCreate> pending,
        CancellationToken ct)
    {
        if (pending.Count == 0) return 0;

        var planned = pending
            .Select(p => new CloudBatchCreate.PlannedItem(p.TempId, p.Code, [p.ParentId], p.Name))
            .ToList();

        IReadOnlyList<Guid> ids;
        if (CloudBatchCreate.ShouldUseBatch(planned.Count))
        {
            if (!CloudBatchCreate.ParentsBeforeChildren(planned))
            {
                throw new InvalidOperationException(
                    "AddEntities batch requires parents-before-children TempId order.");
            }

            ids = await _api.AddEntitiesAsync(CloudBatchCreate.ToContracts(planned));
        }
        else
        {
            var only = pending[0];
            var id = await _api.AddEntityAsync(only.Code, [only.ParentId], only.Name);
            ids = [id];
        }

        var applied = 0;
        for (var i = 0; i < pending.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var p = pending[i];
            var id = ids[i];
            try
            {
                if (!p.IsFolder)
                {
                    if (!File.Exists(p.FullPath))
                    {
                        SyncMissingLocalPath.Log("ApplyLocalPhase.Added", p.RelativePath, p.FullPath);
                        continue;
                    }

                    await using var content = await MappedFolderIoGate.OpenUploadStreamAsync(p.FullPath, ct);
                    await _api.AddFileAsync(id, p.Name, content, MimeTypes.GetMimeType(p.Name));
                }

                var addHash = "";
                if (!p.IsFolder)
                {
                    if (p.ContentHash.Length > 0)
                        addHash = p.ContentHash;
                    else
                    {
                        addHash = ContentHashUtil.TrySha256HexFile(p.FullPath) ?? "";
                        if (addHash.Length == 0)
                            SyncMissingLocalPath.Log("ApplyLocalPhase.Added", p.RelativePath, p.FullPath);
                    }
                }

                _store.Upsert(new SyncItemMeta
                {
                    MappingId = mapping.Id,
                    RemoteId = id,
                    ParentRemoteId = p.ParentId,
                    RelativePath = p.RelativePath,
                    IsFolder = p.IsFolder,
                    SizeBytes = p.SizeBytes,
                    ContentHash = addHash,
                    RemoteUpdateTime = DateTime.UtcNow
                });
                applied++;
            }
            catch (Exception ex) when (SyncMissingLocalPath.IsMissing(ex))
            {
                SyncMissingLocalPath.Log("ApplyLocalPhase.Added", p.RelativePath, p.FullPath, ex);
            }
        }

        return applied;
    }

    /// <summary>Overwrite server with all local files for a mapping; meta follows local.</summary>
    public Task ForcePushMappingAsync(SyncMappingInfo mapping, CancellationToken ct = default) =>
        _store.RunInBatchAsync(() => ForcePushMappingCoreAsync(mapping, ct));

    private async Task ForcePushMappingCoreAsync(SyncMappingInfo mapping, CancellationToken ct)
    {
        if (!Directory.Exists(mapping.LocalRootPath))
            throw new SyncException($"Local path does not exist: {mapping.LocalRootPath}");

        var fs = SnapshotFs(mapping.LocalRootPath)
            .OrderBy(e => e.RelativePath.Count(c => c == '/'))
            .ThenBy(e => e.IsFolder ? 0 : 1)
            .ToList();
        var siblingCache = new Dictionary<Guid, List<RemoteEntryInfo>>();
        var emptyWalk = new List<RemoteLocated>();

        foreach (var entry in fs)
        {
            ct.ThrowIfCancellationRequested();
            var path = SyncPathUtil.Normalize(entry.RelativePath);
            var parentPath = SyncPathUtil.ParentOf(path);
            var parentId = ResolveParentId(mapping, parentPath) ?? mapping.CloudFolderId;
            var name = SyncPathUtil.NameOf(path);
            var existing = _store.GetAll(mapping.Id)
                .FirstOrDefault(i => SyncPathUtil.Normalize(i.RelativePath) == path);

            if (entry.IsFolder)
            {
                if (existing != null) continue;
                var (reuseFolder, folderListingOk) = await ResolveExistingSiblingAsync(
                    parentId, name, isFolder: true, emptyWalk, siblingCache, ct);
                if (reuseFolder == null && !folderListingOk) continue;
                var id = reuseFolder?.Id
                    ?? await _api.AddEntityAsync(CloudEntityCodes.CloudFolder, new[] { parentId }, name);
                if (reuseFolder == null)
                    RememberSibling(siblingCache, parentId, id, name, isFolder: true);
                _store.Upsert(new SyncItemMeta
                {
                    MappingId = mapping.Id,
                    RemoteId = id,
                    ParentRemoteId = parentId,
                    RelativePath = path,
                    IsFolder = true,
                    SizeBytes = 0,
                    ContentHash = "",
                    RemoteUpdateTime = DateTime.UtcNow
                });
                continue;
            }

            var full = ToFull(mapping.LocalRootPath, path);
            try
            {
                var hash = ContentHashUtil.TrySha256HexFile(full);
                if (hash == null)
                {
                    SyncMissingLocalPath.Log("ForcePush", path, full);
                    continue;
                }
                var len = new FileInfo(full).Length;
                Guid remoteId;
                if (existing != null)
                {
                    remoteId = existing.RemoteId;
                    await using var content = await MappedFolderIoGate.OpenUploadStreamAsync(full, ct);
                    await _api.AddFileAsync(remoteId, name, content, MimeTypes.GetMimeType(name));
                }
                else
                {
                    var (reuseFile, fileListingOk) = await ResolveExistingSiblingAsync(
                        parentId, name, isFolder: false, emptyWalk, siblingCache, ct);
                    if (reuseFile == null && !fileListingOk)
                        continue;
                    if (reuseFile != null)
                    {
                        remoteId = reuseFile.Id;
                        await using var content = await MappedFolderIoGate.OpenUploadStreamAsync(full, ct);
                        await _api.AddFileAsync(remoteId, name, content, MimeTypes.GetMimeType(name));
                    }
                    else
                    {
                        remoteId = await _api.AddEntityAsync(CloudEntityCodes.CloudFile, new[] { parentId }, name);
                        await using var content = await MappedFolderIoGate.OpenUploadStreamAsync(full, ct);
                        await _api.AddFileAsync(remoteId, name, content, MimeTypes.GetMimeType(name));
                        RememberSibling(siblingCache, parentId, remoteId, name, isFolder: false);
                    }
                }

                _store.Upsert(new SyncItemMeta
                {
                    MappingId = mapping.Id,
                    RemoteId = remoteId,
                    ParentRemoteId = parentId,
                    RelativePath = path,
                    IsFolder = false,
                    SizeBytes = len,
                    ContentHash = hash,
                    RemoteUpdateTime = DateTime.UtcNow
                });
            }
            catch (Exception ex) when (SyncMissingLocalPath.IsMissing(ex))
            {
                SyncMissingLocalPath.Log("ForcePush", path, full, ex);
            }
        }

        _store.SetLastSyncUtc(mapping.Id, DateTime.UtcNow);
    }

    /// <summary>Overwrite local FS with all remote files for a mapping; meta follows remote.</summary>
    public Task ForcePullMappingAsync(SyncMappingInfo mapping, CancellationToken ct = default) =>
        _store.RunInBatchAsync(() => ForcePullMappingCoreAsync(mapping, ct));

    /// <summary>
    /// Single-pass FS snapshot: one file enumeration + ancestor folder size rollup.
    /// Legacy per-directory recursive GetFiles was O(dirs×files) and starved Sync on large trees.
    /// When <paramref name="computeContentHashes"/> is false (reconcile local phase), skip SHA-256 —
    /// content convergence already ran in ApplyCompare; hashing ~675MB MyNotesFolder again starved LastSync.
    /// <paramref name="skipRelativePath"/> omits matching paths (AutoPush Agents queue exclude).
    /// </summary>
    public static List<FsEntry> SnapshotFs(
        string rootPath,
        bool computeContentHashes = true,
        Func<string, bool>? skipRelativePath = null)
    {
        var result = new List<FsEntry>();
        if (!Directory.Exists(rootPath)) return result;
        var rootFull = Path.GetFullPath(rootPath);

        var folderSizes = new Dictionary<string, long>(StringComparer.Ordinal);
        try
        {
            foreach (var dir in Directory.GetDirectories(rootFull, "*", SearchOption.AllDirectories))
            {
                var rel = SyncPathUtil.Normalize(Path.GetRelativePath(rootFull, dir));
                if (skipRelativePath?.Invoke(rel) == true) continue;
                folderSizes[rel] = 0;
            }
        }
        catch (Exception ex) when (SyncMissingLocalPath.IsMissing(ex))
        {
            SyncMissingLocalPath.Log("SnapshotFs", "", rootFull, ex);
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(rootFull, "*", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (SyncMissingLocalPath.IsMissing(ex))
        {
            SyncMissingLocalPath.Log("SnapshotFs", "", rootFull, ex);
            files = Array.Empty<string>();
        }

        foreach (var file in files)
        {
            var rel = SyncPathUtil.Normalize(Path.GetRelativePath(rootFull, file));
            if (skipRelativePath?.Invoke(rel) == true) continue;
            try
            {
                var info = new FileInfo(file);
                string hash = "";
                if (computeContentHashes)
                {
                    var hashed = ContentHashUtil.TrySha256HexFile(file);
                    if (hashed == null)
                    {
                        SyncMissingLocalPath.Log("SnapshotFs", rel, file);
                        continue;
                    }
                    hash = hashed;
                }
                else if (!File.Exists(file))
                {
                    SyncMissingLocalPath.Log("SnapshotFs", rel, file);
                    continue;
                }

                result.Add(new FsEntry
                {
                    RelativePath = rel,
                    IsFolder = false,
                    SizeBytes = info.Length,
                    ContentHash = hash
                });

                // Roll size into every ancestor folder (single pass).
                var parent = SyncPathUtil.ParentOf(rel);
                while (!string.IsNullOrEmpty(parent))
                {
                    if (skipRelativePath?.Invoke(parent) == true)
                        break;
                    folderSizes[parent] = folderSizes.GetValueOrDefault(parent) + info.Length;
                    parent = SyncPathUtil.ParentOf(parent);
                }
            }
            catch (Exception ex) when (SyncMissingLocalPath.IsMissing(ex))
            {
                SyncMissingLocalPath.Log("SnapshotFs", rel, file, ex);
            }
        }

        foreach (var (rel, size) in folderSizes)
            result.Add(new FsEntry { RelativePath = rel, IsFolder = true, SizeBytes = size });

        return result;
    }

    private static string ToFull(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
}
