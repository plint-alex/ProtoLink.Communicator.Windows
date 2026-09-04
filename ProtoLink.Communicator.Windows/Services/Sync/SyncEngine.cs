using System.IO;
using ProtoLink.Communicator.Windows.Core;
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
        CancellationToken ct = default,
        bool compareSizeAndTime = true)
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

        if (compareSizeAndTime)
        {
            await EnrichRemoteSizesAsync(remoteLocated, ct);
            foreach (var mapping in mappings)
            {
                ct.ThrowIfCancellationRequested();
                var dirty = new HashSet<string>(StringComparer.Ordinal);
                await ApplyComparePhaseAsync(mapping, remoteLocated, dirty, ct);
                await ApplyLocalPhaseAsync(mapping, dirty, skipSizeUpdates: true, ct);
                dirtyByMapping[mapping.Id] = dirty;
            }
            foreach (var mapping in mappings)
            {
                ct.ThrowIfCancellationRequested();
                await ApplyRemotePhaseAsync(mapping, byId, remoteLocated, dirtyByMapping[mapping.Id], ct);
                _store.SetLastSyncUtc(mapping.Id, DateTime.UtcNow);
            }
        }
        else
        {
            foreach (var mapping in mappings)
            {
                ct.ThrowIfCancellationRequested();
                var dirty = new HashSet<string>(StringComparer.Ordinal);
                await ApplyLocalPhaseAsync(mapping, dirty, skipSizeUpdates: false, ct);
                dirtyByMapping[mapping.Id] = dirty;
            }
            foreach (var mapping in mappings)
            {
                ct.ThrowIfCancellationRequested();
                await ApplyRemotePhaseAsync(mapping, byId, remoteLocated, dirtyByMapping[mapping.Id], ct);
                _store.SetLastSyncUtc(mapping.Id, DateTime.UtcNow);
            }
        }
    }

    private async Task EnrichRemoteSizesAsync(List<RemoteLocated> located, CancellationToken ct)
    {
        foreach (var loc in located)
        {
            ct.ThrowIfCancellationRequested();
            if (loc.Entry.IsFolder || loc.Entry.SizeBytes != null) continue;
            loc.Entry.SizeBytes = await _api.GetFileContentLengthOrNullAsync(loc.Entry.Id, ct);
        }
    }

    private async Task ApplyComparePhaseAsync(
        SyncMappingInfo mapping,
        List<RemoteLocated> allRemote,
        HashSet<string> dirty,
        CancellationToken ct)
    {
        var snap = SnapshotFs(mapping.LocalRootPath)
            .ToDictionary(e => SyncPathUtil.Normalize(e.RelativePath), StringComparer.Ordinal);
        var remoteById = allRemote.ToDictionary(r => r.Entry.Id);

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

            switch (SyncDirectionDecide.Decide(
                localSize,
                located.Entry.SizeBytes,
                meta.SizeBytes,
                located.Entry.UpdateTime,
                meta.RemoteUpdateTime))
            {
                case SyncDirectionDecide.Action.Write:
                {
                    var name = SyncPathUtil.NameOf(meta.RelativePath);
                    var len = new FileInfo(full).Length;
                    if (len == 0L && (located.Entry.SizeBytes ?? 0L) > 0L) break;
                    await using var content = File.OpenRead(full);
                    await _api.AddFileAsync(meta.RemoteId, name, content, MimeTypes.GetMimeType(name));
                    meta.SizeBytes = len;
                    meta.RemoteUpdateTime = DateTime.UtcNow;
                    _store.Upsert(meta);
                    dirty.Add(path);
                    break;
                }
                case SyncDirectionDecide.Action.Read:
                {
                    await using var stream = await _api.GetFileStreamOrNotFoundAsync(meta.RemoteId, ct);
                    if (stream == null) break;
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    await using (var fs = File.Create(full))
                        await stream.CopyToAsync(fs, ct);
                    var newLen = new FileInfo(full).Length;
                    if (newLen == 0L && localSize > 0L) break;
                    meta.SizeBytes = newLen;
                    meta.RemoteUpdateTime = located.Entry.UpdateTime ?? DateTime.UtcNow;
                    _store.Upsert(meta);
                    dirty.Add(path);
                    break;
                }
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
                long? existingSize = missing ? null : new FileInfo(full).Length;
                var action = SyncDirectionDecide.Decide(
                    existingSize,
                    loc.Entry.SizeBytes,
                    existingSize ?? 0L,
                    loc.Entry.UpdateTime,
                    metaTime: null);
                if (action == SyncDirectionDecide.Action.Read || missing)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    await using var stream = await _api.GetFileStreamOrNotFoundAsync(loc.Entry.Id, ct);
                    if (stream != null)
                    {
                        await using var fs = File.Create(full);
                        await stream.CopyToAsync(fs, ct);
                    }
                    else if (missing)
                        continue; // no empty placeholders
                }
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
                    RemoteUpdateTime = loc.Entry.UpdateTime
                });
            }
        }
        RecomputeFolderSizes(mapping);
    }

    private void RecomputeFolderSizes(SyncMappingInfo mapping)
    {
        var snap = SnapshotFs(mapping.LocalRootPath).ToDictionary(e => SyncPathUtil.Normalize(e.RelativePath));
        foreach (var item in _store.GetAll(mapping.Id).Where(i => i.IsFolder))
        {
            if (snap.TryGetValue(SyncPathUtil.Normalize(item.RelativePath), out var fs) && fs.SizeBytes != item.SizeBytes)
            {
                item.SizeBytes = fs.SizeBytes;
                _store.Upsert(item);
            }
        }
    }

    private async Task ApplyLocalPhaseAsync(
        SyncMappingInfo mapping,
        HashSet<string> dirty,
        bool skipSizeUpdates,
        CancellationToken ct)
    {
        var snap = SnapshotFs(mapping.LocalRootPath);
        var meta = _store.GetAll(mapping.Id);
        var changes = _localClassifier.Classify(snap, meta);

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
                RemoteUpdateTime = r.Meta.RemoteUpdateTime
            });
            dirty.Add(r.NewRelativePath);
            dirty.Add(SyncPathUtil.Normalize(r.Meta.RelativePath));
        }

        foreach (var a in changes.OfType<LocalChange.Added>().OrderBy(x => x.Entry.RelativePath.Count(c => c == '/')))
        {
            ct.ThrowIfCancellationRequested();
            var parentPath = SyncPathUtil.ParentOf(a.Entry.RelativePath);
            var parentId = ResolveParentId(mapping, parentPath) ?? mapping.CloudFolderId;
            var name = SyncPathUtil.NameOf(a.Entry.RelativePath);
            var full = ToFull(mapping.LocalRootPath, a.Entry.RelativePath);
            Guid id;
            if (a.Entry.IsFolder)
            {
                id = await _api.AddEntityAsync(CloudEntityCodes.CloudFolder, new[] { parentId }, name);
            }
            else
            {
                id = await _api.AddEntityAsync(CloudEntityCodes.CloudFile, new[] { parentId }, name);
                await using var content = File.OpenRead(full);
                await _api.AddFileAsync(id, name, content, MimeTypes.GetMimeType(name));
            }
            _store.Upsert(new SyncItemMeta
            {
                MappingId = mapping.Id,
                RemoteId = id,
                ParentRemoteId = parentId,
                RelativePath = SyncPathUtil.Normalize(a.Entry.RelativePath),
                IsFolder = a.Entry.IsFolder,
                SizeBytes = a.Entry.SizeBytes,
                RemoteUpdateTime = DateTime.UtcNow
            });
            dirty.Add(SyncPathUtil.Normalize(a.Entry.RelativePath));
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
            if (skipSizeUpdates) continue;
            var name = SyncPathUtil.NameOf(u.Meta.RelativePath);
            var full = ToFull(mapping.LocalRootPath, u.Meta.RelativePath);
            await using var content = File.OpenRead(full);
            await _api.AddFileAsync(u.Meta.RemoteId, name, content, MimeTypes.GetMimeType(name));
            u.Meta.SizeBytes = u.NewSize;
            u.Meta.RemoteUpdateTime = DateTime.UtcNow;
            _store.Upsert(u.Meta);
            dirty.Add(SyncPathUtil.Normalize(u.Meta.RelativePath));
        }

        foreach (var rm in changes.OfType<LocalChange.Removed>().OrderByDescending(x => x.Meta.RelativePath.Count(c => c == '/')))
        {
            ct.ThrowIfCancellationRequested();
            await _api.DeleteEntityAsync(rm.Meta.RemoteId);
            _store.Delete(mapping.Id, rm.Meta.RelativePath);
            dirty.Add(SyncPathUtil.Normalize(rm.Meta.RelativePath));
        }
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
                        RemoteUpdateTime = upd.Remote.UpdateTime ?? DateTime.UtcNow
                    });
                    break;
                }
            }
        }
    }

    private async Task WalkRemoteAsync(Guid folderId, string mappingId, string relativeParent, List<RemoteLocated> outList, CancellationToken ct)
    {
        var children = await _api.GetEntitiesAsync(new[] { folderId }, includeValues: true);
        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            var isFolder = child.Code == CloudEntityCodes.CloudFolder;
            var isFile = child.Code == CloudEntityCodes.CloudFile;
            if (!isFolder && !isFile) continue;
            var name = CloudApiService.GetDisplayName(child);
            if (string.IsNullOrEmpty(name) || CloudApiService.IsSyntheticCloudFileDisplayName(child, name))
                continue;
            var rel = SyncPathUtil.Join(relativeParent, name);
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
                await WalkRemoteAsync(child.Id, mappingId, rel, outList, ct);
        }
    }

    private Guid? ResolveParentId(SyncMappingInfo mapping, string parentPath)
    {
        if (string.IsNullOrEmpty(parentPath)) return mapping.CloudFolderId;
        return _store.GetAll(mapping.Id)
            .FirstOrDefault(i => SyncPathUtil.Normalize(i.RelativePath) == SyncPathUtil.Normalize(parentPath))
            ?.RemoteId;
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
            result.Add(new FsEntry { RelativePath = rel, IsFolder = false, SizeBytes = new FileInfo(file).Length });
        }
        return result;
    }

    private static string ToFull(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
}
