using System.IO;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;

namespace ProtoLink.Communicator.Windows.Services.Sync;

/// <summary>3-way reconcile: FS ↔ metadata store ↔ remote. Local wins. Mirrors Android SyncEngine.</summary>
public sealed class SyncEngine
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

    public async Task ReconcileAllAsync(
        IReadOnlyList<SyncMappingInfo> mappings,
        CancellationToken ct = default)
    {
        var dirtyByMapping = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var mapping in mappings)
        {
            ct.ThrowIfCancellationRequested();
            if (_store.GetAll(mapping.Id).Count == 0)
                await BootstrapFromRemoteAsync(mapping, ct);
        }

        var remoteLocated = new List<RemoteLocated>();
        foreach (var mapping in mappings)
            await WalkRemoteAsync(mapping.CloudFolderId, mapping.Id, "", remoteLocated, ct);

        var byId = mappings.ToDictionary(m => m.Id);

        // Always hash-compare every mapped file so all devices converge to the same bytes.
        await EnrichRemoteSizesAsync(remoteLocated, ct);
        foreach (var mapping in mappings)
        {
            ct.ThrowIfCancellationRequested();
            var dirty = new HashSet<string>(StringComparer.Ordinal);
            await ApplyComparePhaseAsync(mapping, remoteLocated, dirty, ct);
            var remoteForMapping = remoteLocated.Where(x => x.MappingId == mapping.Id).ToList();
            await ApplyLocalPhaseAsync(mapping, dirty, skipSizeUpdates: true, remoteForMapping, ct);
            dirtyByMapping[mapping.Id] = dirty;
        }
        foreach (var mapping in mappings)
        {
            ct.ThrowIfCancellationRequested();
            await ApplyRemotePhaseAsync(mapping, byId, remoteLocated, dirtyByMapping[mapping.Id], ct);
            _store.SetLastSyncUtc(mapping.Id, DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Upload local FS changes vs meta only (no remote walk / download).
    /// Mappings with empty metadata are fully reconciled first (never blind-create remote entities).
    /// Returns number of local structural/content ops applied from pure push passes.
    /// </summary>
    public async Task<int> PushLocalChangesAsync(
        IReadOnlyList<SyncMappingInfo> mappings,
        CancellationToken ct = default)
    {
        var applied = 0;
        var needFull = new List<SyncMappingInfo>();
        var ready = new List<SyncMappingInfo>();
        foreach (var mapping in mappings)
        {
            if (!Directory.Exists(mapping.LocalRootPath)) continue;
            if (_store.GetAll(mapping.Id).Count == 0)
                needFull.Add(mapping);
            else
                ready.Add(mapping);
        }

        if (needFull.Count > 0)
            await ReconcileAllAsync(needFull, ct);

        foreach (var mapping in ready)
        {
            ct.ThrowIfCancellationRequested();
            var dirty = new HashSet<string>(StringComparer.Ordinal);
            applied += await ApplyLocalPhaseAsync(
                mapping, dirty, skipSizeUpdates: false, remoteInMapping: new List<RemoteLocated>(), ct);
            _store.SetLastSyncUtc(mapping.Id, DateTime.UtcNow);
        }
        return applied;
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
        var snap = SnapshotFs(mapping.LocalRootPath)
            .GroupBy(e => SyncPathUtil.Normalize(e.RelativePath), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
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
                await using var stream = await _api.GetFileStreamOrNotFoundAsync(meta.RemoteId, ct);
                if (stream != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    await using (var fs = File.Create(full))
                        await stream.CopyToAsync(fs, ct);
                    var newLen = new FileInfo(full).Length;
                    if (newLen > 0L)
                    {
                        meta.SizeBytes = newLen;
                        meta.ContentHash = ContentHashUtil.Sha256HexFile(full);
                        meta.RemoteUpdateTime = located.Entry.UpdateTime ?? DateTime.UtcNow;
                        _store.Upsert(meta);
                        dirty.Add(path);
                    }
                }
                continue;
            }

            var localHash = ContentHashUtil.Sha256HexFile(full);

            // Always download remote bytes and compare hashes. Never forge remoteHash from
            // meta or upload when remote content is unknown — that left devices on Test4
            // while cloud had Test5 (and the reverse overwrite).
            byte[]? remoteBytes = null;
            await using (var stream = await _api.GetFileStreamOrNotFoundAsync(meta.RemoteId, ct))
            {
                if (stream != null)
                {
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms, ct);
                    remoteBytes = ms.ToArray();
                }
            }
            if (remoteBytes == null || remoteBytes.Length == 0)
            {
                throw new SyncException(
                    "Remote download failed or returned empty; cannot reconcile file.",
                    relativePath: path,
                    localHash: localHash);
            }

            var remoteHash = ContentHashUtil.Sha256Hex(remoteBytes);
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
                    await using var content = File.OpenRead(full);
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
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    await File.WriteAllBytesAsync(full, remoteBytes, ct);
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
                case SyncDirectionDecide.Action.Conflict:
                    throw new SyncConflictException(
                        path,
                        localHash,
                        remoteHash,
                        meta.ContentHash,
                        "Local and remote both differ from sync baseline (or baseline is empty). Use Force Upload or Force Download.");
            }
        }
    }

    private async Task BootstrapFromRemoteAsync(SyncMappingInfo mapping, CancellationToken ct)
    {
        var located = new List<RemoteLocated>();
        await WalkRemoteAsync(mapping.CloudFolderId, mapping.Id, "", located, ct);
        await EnrichRemoteSizesAsync(located, ct);
        foreach (var loc in located.OrderBy(l => l.RelativePath.Count(c => c == '/')))
        {
            ct.ThrowIfCancellationRequested();
            var full = Path.Combine(mapping.LocalRootPath, loc.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (loc.Entry.IsFolder)
            {
                Directory.CreateDirectory(full);
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
            }
            else
            {
                var missing = !File.Exists(full);
                // Always pull when missing; when present, download and keep remote if hashes differ
                // (bootstrap has no meta baseline — prefer cloud so devices share one tree).
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                await using var stream = await _api.GetFileStreamOrNotFoundAsync(loc.Entry.Id, ct);
                if (stream != null)
                {
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms, ct);
                    var remoteBytes = ms.ToArray();
                    if (remoteBytes.Length > 0)
                    {
                        if (missing)
                        {
                            await File.WriteAllBytesAsync(full, remoteBytes, ct);
                        }
                        else
                        {
                            var localHash = ContentHashUtil.Sha256HexFile(full);
                            var remoteHash = ContentHashUtil.Sha256Hex(remoteBytes);
                            if (!string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase))
                                await File.WriteAllBytesAsync(full, remoteBytes, ct);
                        }
                    }
                    else if (missing)
                        continue; // no empty placeholders
                }
                else if (missing)
                    continue;
                var len = File.Exists(full) ? new FileInfo(full).Length : 0L;
                if (missing && len == 0L && !File.Exists(full)) continue;
                _store.Upsert(new SyncItemMeta
                {
                    MappingId = mapping.Id,
                    RemoteId = loc.Entry.Id,
                    ParentRemoteId = loc.Entry.ParentId,
                    RelativePath = loc.RelativePath,
                    IsFolder = false,
                    SizeBytes = len,
                    ContentHash = File.Exists(full) ? ContentHashUtil.Sha256HexFile(full) : "",
                    RemoteUpdateTime = loc.Entry.UpdateTime
                });
            }
        }
        RecomputeFolderSizes(mapping);
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
        CancellationToken ct)
    {
        var applied = 0;
        var snap = SnapshotFs(mapping.LocalRootPath);
        var meta = _store.GetAll(mapping.Id);
        var changes = _localClassifier.Classify(snap, meta);
        // Cloud can list the same display name twice (or multi-parent graphs); keep newest.
        var remoteByPath = remoteInMapping
            .GroupBy(x => SyncPathUtil.Normalize(x.RelativePath), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.Entry.UpdateTime ?? DateTime.MinValue).First(),
                StringComparer.Ordinal);

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

        foreach (var a in changes.OfType<LocalChange.Added>().OrderBy(x => x.Entry.RelativePath.Count(c => c == '/')))
        {
            ct.ThrowIfCancellationRequested();
            var path = SyncPathUtil.Normalize(a.Entry.RelativePath);
            if (remoteByPath.TryGetValue(path, out var existingRemote))
            {
                var name = SyncPathUtil.NameOf(a.Entry.RelativePath);
                var full = ToFull(mapping.LocalRootPath, a.Entry.RelativePath);
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
                    var len = new FileInfo(full).Length;
                    var remoteSize = existingRemote.Entry.SizeBytes;
                    var localLooksStub = len == 0 || (len < 64 && remoteSize is > 0 && remoteSize > len);
                    if (localLooksStub)
                    {
                        await using var cloud = await _api.GetFileStreamOrNotFoundAsync(existingRemote.Entry.Id, ct);
                        if (cloud != null)
                        {
                            await using var fs = File.Create(full);
                            await cloud.CopyToAsync(fs, ct);
                            var written = new FileInfo(full).Length;
                            _store.Upsert(new SyncItemMeta
                            {
                                MappingId = mapping.Id,
                                RemoteId = existingRemote.Entry.Id,
                                ParentRemoteId = existingRemote.Entry.ParentId,
                                RelativePath = path,
                                IsFolder = false,
                                SizeBytes = written,
                                ContentHash = ContentHashUtil.Sha256HexFile(full),
                                RemoteUpdateTime = existingRemote.Entry.UpdateTime ?? DateTime.UtcNow
                            });
                            dirty.Add(path);
                            applied++;
                        }
                        continue;
                    }
                    // Adopt existing remote id: converge by content hash (never upload solely because size is unknown).
                    byte[]? remoteBytes = null;
                    await using (var cloud = await _api.GetFileStreamOrNotFoundAsync(existingRemote.Entry.Id, ct))
                    {
                        if (cloud != null)
                        {
                            using var ms = new MemoryStream();
                            await cloud.CopyToAsync(ms, ct);
                            remoteBytes = ms.ToArray();
                        }
                    }
                    if (remoteBytes == null || remoteBytes.Length == 0)
                    {
                        throw new SyncException(
                            "Remote download failed or returned empty while adopting local add.",
                            relativePath: path);
                    }
                    var localHash = ContentHashUtil.Sha256HexFile(full);
                    var remoteHash = ContentHashUtil.Sha256Hex(remoteBytes);
                    var action = ContentHashUtil.DecideByHash(
                        localHash, remoteHash, metaHash: null,
                        existingRemote.Entry.UpdateTime, metaUpdateTime: null);
                    if (action == SyncDirectionDecide.Action.Conflict)
                    {
                        throw new SyncConflictException(
                            path,
                            localHash,
                            remoteHash,
                            null,
                            "Local add collides with different remote content. Use Force Upload or Force Download.");
                    }
                    if (action == SyncDirectionDecide.Action.Write)
                    {
                        await using var content = File.OpenRead(full);
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
                        if (action == SyncDirectionDecide.Action.Read)
                            await File.WriteAllBytesAsync(full, remoteBytes, ct);
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
            var parentId = ResolveParentId(mapping, parentPath) ?? mapping.CloudFolderId;
            var addName = SyncPathUtil.NameOf(a.Entry.RelativePath);
            var addFull = ToFull(mapping.LocalRootPath, a.Entry.RelativePath);
            Guid id;
            if (a.Entry.IsFolder)
            {
                id = await _api.AddEntityAsync(CloudEntityCodes.CloudFolder, new[] { parentId }, addName);
            }
            else
            {
                if (new FileInfo(addFull).Length == 0) continue;
                id = await _api.AddEntityAsync(CloudEntityCodes.CloudFile, new[] { parentId }, addName);
                await using var content = File.OpenRead(addFull);
                await _api.AddFileAsync(id, addName, content, MimeTypes.GetMimeType(addName));
            }
            _store.Upsert(new SyncItemMeta
            {
                MappingId = mapping.Id,
                RemoteId = id,
                ParentRemoteId = parentId,
                RelativePath = SyncPathUtil.Normalize(a.Entry.RelativePath),
                IsFolder = a.Entry.IsFolder,
                SizeBytes = a.Entry.SizeBytes,
                ContentHash = a.Entry.IsFolder ? "" : (a.Entry.ContentHash.Length > 0 ? a.Entry.ContentHash : ContentHashUtil.Sha256HexFile(addFull)),
                RemoteUpdateTime = DateTime.UtcNow
            });
            dirty.Add(SyncPathUtil.Normalize(a.Entry.RelativePath));
            applied++;
        }

        foreach (var u in changes.OfType<LocalChange.Updated>())
        {
            ct.ThrowIfCancellationRequested();
            if (u.Meta.IsFolder)
            {
                u.Meta.SizeBytes = u.NewSize;
                _store.Upsert(u.Meta);
                dirty.Add(SyncPathUtil.Normalize(u.Meta.RelativePath));
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
            var name = SyncPathUtil.NameOf(u.Meta.RelativePath);
            var full = ToFull(mapping.LocalRootPath, u.Meta.RelativePath);
            await using var content2 = File.OpenRead(full);
            await _api.AddFileAsync(u.Meta.RemoteId, name, content2, MimeTypes.GetMimeType(name));
            u.Meta.SizeBytes = u.NewSize;
            u.Meta.ContentHash = ContentHashUtil.Sha256HexFile(full);
            u.Meta.RemoteUpdateTime = DateTime.UtcNow;
            _store.Upsert(u.Meta);
            dirty.Add(SyncPathUtil.Normalize(u.Meta.RelativePath));
            applied++;
        }

        foreach (var rm in changes.OfType<LocalChange.Removed>().OrderByDescending(x => x.Meta.RelativePath.Count(c => c == '/')))
        {
            ct.ThrowIfCancellationRequested();
            await _api.DeleteEntityAsync(rm.Meta.RemoteId);
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
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(toFull)!);
                                if (Directory.Exists(toFull)) Directory.Delete(toFull, true);
                                Directory.Move(fromFull, toFull);
                            }
                            else
                                Directory.CreateDirectory(toFull);
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(toFull)!);
                            if (File.Exists(fromFull))
                            {
                                if (File.Exists(toFull)) File.Delete(toFull);
                                File.Move(fromFull, toFull);
                            }
                            else
                            {
                                await using var stream = await _api.GetFileStreamOrNotFoundAsync(ren.Meta.RemoteId, ct);
                                if (stream != null)
                                {
                                    await using var fs = File.Create(toFull);
                                    await stream.CopyToAsync(fs, ct);
                                }
                            }
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
                        {
                            if (Directory.Exists(toFull)) Directory.Delete(toFull, true);
                            Directory.Move(fromFull, toFull);
                        }
                        else if (!ren.Meta.IsFolder && File.Exists(fromFull))
                        {
                            if (File.Exists(toFull)) File.Delete(toFull);
                            File.Move(fromFull, toFull);
                        }
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
                        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                        await using var stream = await _api.GetFileStreamOrNotFoundAsync(add.Remote.Id, ct);
                        await using var fs = File.Create(full);
                        if (stream != null) await stream.CopyToAsync(fs, ct);
                        _store.Upsert(new SyncItemMeta
                        {
                            MappingId = mapping.Id,
                            RemoteId = add.Remote.Id,
                            ParentRemoteId = add.Remote.ParentId,
                            RelativePath = add.RelativePath,
                            IsFolder = false,
                            SizeBytes = new FileInfo(full).Length,
                            ContentHash = File.Exists(full) ? ContentHashUtil.Sha256HexFile(full) : "",
                            RemoteUpdateTime = add.Remote.UpdateTime
                        });
                    }
                    break;
                }
                case RemoteChange.Removed rem:
                {
                    if (rem.Meta.MappingId != mapping.Id) break;
                    if (cross.Any(x => x.Entry.Id == rem.Meta.RemoteId)) break;
                    var full = ToFull(mapping.LocalRootPath, rem.Meta.RelativePath);
                    if (rem.Meta.IsFolder && Directory.Exists(full)) Directory.Delete(full, true);
                    else if (!rem.Meta.IsFolder && File.Exists(full)) File.Delete(full);
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
                    await using var stream = await _api.GetFileStreamOrNotFoundAsync(upd.Remote.Id, ct);
                    if (stream == null) break;
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    await using (var fs = File.Create(full))
                        await stream.CopyToAsync(fs, ct);
                    _store.Upsert(new SyncItemMeta
                    {
                        MappingId = mapping.Id,
                        RemoteId = upd.Meta.RemoteId,
                        ParentRemoteId = upd.Meta.ParentRemoteId,
                        RelativePath = upd.Meta.RelativePath,
                        IsFolder = false,
                        SizeBytes = new FileInfo(full).Length,
                        ContentHash = ContentHashUtil.Sha256HexFile(full),
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
        HashSet<string>? seenPaths = null)
    {
        visitedIds ??= new HashSet<Guid>();
        seenPaths ??= new HashSet<string>(StringComparer.Ordinal);
        // Parallel folder listings: MyNotesFolder has many children and sequential
        // GetEntities(includeValues) was taking minutes (70–95 entities per notes folder).
        var gate = new object();
        using var throttle = new SemaphoreSlim(2);
        await WalkRemoteParallelAsync(
            folderId, mappingId, relativeParent, outList, ct, visitedIds, seenPaths, gate, throttle);
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
        SemaphoreSlim throttle)
    {
        await throttle.WaitAsync(ct);
        List<GetEntitiesResult> children;
        try
        {
            children = await _api.GetEntitiesAsync(new[] { folderId }, includeValues: true);
        }
        finally
        {
            throttle.Release();
        }

        var subfolders = new List<(Guid Id, string Rel)>();
        lock (gate)
        {
            foreach (var child in children)
            {
                ct.ThrowIfCancellationRequested();
                var isFolder = child.Code == CloudEntityCodes.CloudFolder;
                var isFile = child.Code == CloudEntityCodes.CloudFile;
                if (!isFolder && !isFile) continue;
                if (!visitedIds.Add(child.Id)) continue;
                var name = CloudApiService.GetDisplayName(child);
                if (string.IsNullOrEmpty(name) || CloudApiService.IsSyntheticCloudFileDisplayName(child, name))
                    continue;
                var rel = SyncPathUtil.Join(relativeParent, name);
                if (!seenPaths.Add(SyncPathUtil.Normalize(rel))) continue;
                var entry = new RemoteEntryInfo
                {
                    Id = child.Id,
                    ParentId = folderId,
                    Name = name,
                    IsFolder = isFolder,
                    UpdateTime = child.UpdateTime == default ? null : child.UpdateTime
                };
                outList.Add(new RemoteLocated { Entry = entry, MappingId = mappingId, RelativePath = rel });
                if (isFolder)
                    subfolders.Add((child.Id, rel));
            }
        }

        if (subfolders.Count == 0) return;
        await Task.WhenAll(subfolders.Select(sf => WalkRemoteParallelAsync(
            sf.Id, mappingId, sf.Rel, outList, ct, visitedIds, seenPaths, gate, throttle)));
    }

    private Guid? ResolveParentId(SyncMappingInfo mapping, string parentPath)
    {
        if (string.IsNullOrEmpty(parentPath)) return mapping.CloudFolderId;
        return _store.GetAll(mapping.Id)
            .FirstOrDefault(i => SyncPathUtil.Normalize(i.RelativePath) == SyncPathUtil.Normalize(parentPath))
            ?.RemoteId;
    }

    /// <summary>Overwrite server with all local files for a mapping; meta follows local.</summary>
    public async Task ForcePushMappingAsync(SyncMappingInfo mapping, CancellationToken ct = default)
    {
        if (!Directory.Exists(mapping.LocalRootPath))
            throw new SyncException($"Local path does not exist: {mapping.LocalRootPath}");

        var fs = SnapshotFs(mapping.LocalRootPath)
            .OrderBy(e => e.RelativePath.Count(c => c == '/'))
            .ThenBy(e => e.IsFolder ? 0 : 1)
            .ToList();

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
                var id = await _api.AddEntityAsync(CloudEntityCodes.CloudFolder, new[] { parentId }, name);
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
            var hash = ContentHashUtil.Sha256HexFile(full);
            var len = new FileInfo(full).Length;
            Guid remoteId;
            if (existing != null)
            {
                remoteId = existing.RemoteId;
                await using var content = File.OpenRead(full);
                await _api.AddFileAsync(remoteId, name, content, MimeTypes.GetMimeType(name));
            }
            else
            {
                remoteId = await _api.AddEntityAsync(CloudEntityCodes.CloudFile, new[] { parentId }, name);
                await using var content = File.OpenRead(full);
                await _api.AddFileAsync(remoteId, name, content, MimeTypes.GetMimeType(name));
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

        _store.SetLastSyncUtc(mapping.Id, DateTime.UtcNow);
    }

    /// <summary>Overwrite local FS with all remote files for a mapping; meta follows remote.</summary>
    public async Task ForcePullMappingAsync(SyncMappingInfo mapping, CancellationToken ct = default)
    {
        Directory.CreateDirectory(mapping.LocalRootPath);
        var located = new List<RemoteLocated>();
        await WalkRemoteAsync(mapping.CloudFolderId, mapping.Id, "", located, ct);

        foreach (var loc in located.OrderBy(l => l.RelativePath.Count(c => c == '/')))
        {
            ct.ThrowIfCancellationRequested();
            var path = SyncPathUtil.Normalize(loc.RelativePath);
            var full = ToFull(mapping.LocalRootPath, path);
            if (loc.Entry.IsFolder)
            {
                Directory.CreateDirectory(full);
                _store.Upsert(new SyncItemMeta
                {
                    MappingId = mapping.Id,
                    RemoteId = loc.Entry.Id,
                    ParentRemoteId = loc.Entry.ParentId,
                    RelativePath = path,
                    IsFolder = true,
                    SizeBytes = 0,
                    ContentHash = "",
                    RemoteUpdateTime = loc.Entry.UpdateTime ?? DateTime.UtcNow
                });
                continue;
            }

            await using var stream = await _api.GetFileStreamOrNotFoundAsync(loc.Entry.Id, ct);
            if (stream == null)
                throw new SyncException("Force download failed: remote file missing.", relativePath: path);

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await using (var fs = File.Create(full))
                await stream.CopyToAsync(fs, ct);
            if (new FileInfo(full).Length == 0)
                throw new SyncException("Force download returned empty file.", relativePath: path);

            var hash = ContentHashUtil.Sha256HexFile(full);
            _store.Upsert(new SyncItemMeta
            {
                MappingId = mapping.Id,
                RemoteId = loc.Entry.Id,
                ParentRemoteId = loc.Entry.ParentId,
                RelativePath = path,
                IsFolder = false,
                SizeBytes = new FileInfo(full).Length,
                ContentHash = hash,
                RemoteUpdateTime = loc.Entry.UpdateTime ?? DateTime.UtcNow
            });
        }

        _store.SetLastSyncUtc(mapping.Id, DateTime.UtcNow);
    }

    public static List<FsEntry> SnapshotFs(string rootPath)
    {
        var result = new List<FsEntry>();
        if (!Directory.Exists(rootPath)) return result;
        var rootFull = Path.GetFullPath(rootPath);

        foreach (var dir in Directory.GetDirectories(rootFull, "*", SearchOption.AllDirectories))
        {
            var rel = SyncPathUtil.Normalize(Path.GetRelativePath(rootFull, dir));
            long size = 0;
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                size += new FileInfo(f).Length;
            result.Add(new FsEntry { RelativePath = rel, IsFolder = true, SizeBytes = size });
        }

        foreach (var file in Directory.GetFiles(rootFull, "*", SearchOption.AllDirectories))
        {
            var rel = SyncPathUtil.Normalize(Path.GetRelativePath(rootFull, file));
            var info = new FileInfo(file);
            result.Add(new FsEntry
            {
                RelativePath = rel,
                IsFolder = false,
                SizeBytes = info.Length,
                ContentHash = ContentHashUtil.Sha256HexFile(file)
            });
        }
        return result;
    }

    private static string ToFull(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
}
