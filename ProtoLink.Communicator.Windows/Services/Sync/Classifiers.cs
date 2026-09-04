namespace ProtoLink.Communicator.Windows.Services.Sync;

public abstract record LocalChange
{
    public sealed record Renamed(SyncItemMeta Meta, string NewRelativePath, string NewName) : LocalChange;
    public sealed record Added(FsEntry Entry) : LocalChange;
    public sealed record Removed(SyncItemMeta Meta) : LocalChange;
    public sealed record Updated(SyncItemMeta Meta, long NewSize) : LocalChange;
}

public abstract record RemoteChange
{
    public sealed record Renamed(SyncItemMeta Meta, RemoteEntryInfo Remote, string NewRelativePath) : RemoteChange;
    public sealed record Added(RemoteEntryInfo Remote, string RelativePath) : RemoteChange;
    public sealed record Removed(SyncItemMeta Meta) : RemoteChange;
    public sealed record Updated(SyncItemMeta Meta, RemoteEntryInfo Remote) : RemoteChange;
}

public sealed class LocalChangeClassifier
{
    public List<LocalChange> Classify(IReadOnlyList<FsEntry> fs, IReadOnlyList<SyncItemMeta> store)
    {
        var fsByPath = fs.ToDictionary(e => SyncPathUtil.Normalize(e.RelativePath), e => e);
        var storeByPath = store.ToDictionary(e => SyncPathUtil.Normalize(e.RelativePath), e => e);
        var onlyFs = fsByPath.Keys.Except(storeByPath.Keys).ToList();
        var onlyStore = storeByPath.Keys.Except(fsByPath.Keys).ToList();
        var both = fsByPath.Keys.Intersect(storeByPath.Keys).ToList();

        var unpairedFs = onlyFs.Select(k => fsByPath[k]).ToList();
        var unpairedStore = onlyStore.Select(k => storeByPath[k]).ToList();
        var sizeCounts = new Dictionary<long, int>();
        foreach (var e in unpairedFs)
            sizeCounts[e.SizeBytes] = sizeCounts.GetValueOrDefault(e.SizeBytes) + 1;
        foreach (var m in unpairedStore)
            sizeCounts[m.SizeBytes] = sizeCounts.GetValueOrDefault(m.SizeBytes) + 1;

        var usedFs = new HashSet<string>(StringComparer.Ordinal);
        var usedStore = new HashSet<string>(StringComparer.Ordinal);
        var changes = new List<LocalChange>();

        foreach (var missing in unpairedStore)
        {
            var key = SyncPathUtil.Normalize(missing.RelativePath);
            if (usedStore.Contains(key)) continue;
            var matches = unpairedFs
                .Where(e => !usedFs.Contains(SyncPathUtil.Normalize(e.RelativePath))
                            && e.IsFolder == missing.IsFolder
                            && e.SizeBytes == missing.SizeBytes)
                .ToList();
            var unique = matches.Count == 1 && sizeCounts.GetValueOrDefault(missing.SizeBytes) == 2;
            if (!unique) continue;
            var neu = matches[0];
            usedFs.Add(SyncPathUtil.Normalize(neu.RelativePath));
            usedStore.Add(key);
            changes.Add(new LocalChange.Renamed(missing, SyncPathUtil.Normalize(neu.RelativePath), SyncPathUtil.NameOf(neu.RelativePath)));
        }

        foreach (var path in onlyFs)
        {
            if (usedFs.Contains(path)) continue;
            changes.Add(new LocalChange.Added(fsByPath[path]));
        }
        foreach (var path in onlyStore)
        {
            if (usedStore.Contains(path)) continue;
            changes.Add(new LocalChange.Removed(storeByPath[path]));
        }
        foreach (var path in both)
        {
            var f = fsByPath[path];
            var s = storeByPath[path];
            if (f.IsFolder != s.IsFolder) continue;
            if (f.SizeBytes != s.SizeBytes)
                changes.Add(new LocalChange.Updated(s, f.SizeBytes));
        }
        return changes;
    }
}

public sealed class RemoteLocated
{
    public required RemoteEntryInfo Entry { get; init; }
    public required string MappingId { get; init; }
    public required string RelativePath { get; init; }
}

public sealed class RemoteChangeClassifier
{
    public List<RemoteChange> Classify(
        IReadOnlyList<SyncItemMeta> store,
        IReadOnlyList<RemoteLocated> remoteLocated,
        HashSet<Guid> allStoreRemoteIds,
        HashSet<string> dirtyLocalPaths)
    {
        var remoteById = remoteLocated.ToDictionary(r => r.Entry.Id);
        var changes = new List<RemoteChange>();

        foreach (var meta in store)
        {
            var dirty = dirtyLocalPaths.Contains(SyncPathUtil.Normalize(meta.RelativePath));
            if (!remoteById.TryGetValue(meta.RemoteId, out var remote))
            {
                if (!dirty) changes.Add(new RemoteChange.Removed(meta));
                continue;
            }
            if (remote.MappingId != meta.MappingId
                || SyncPathUtil.Normalize(remote.RelativePath) != SyncPathUtil.Normalize(meta.RelativePath))
            {
                if (!dirty)
                    changes.Add(new RemoteChange.Renamed(meta, remote.Entry, SyncPathUtil.Normalize(remote.RelativePath)));
                continue;
            }
            var sizeDiffers = remote.Entry.SizeBytes is long rs && rs != meta.SizeBytes;
            var updateNewer = remote.Entry.UpdateTime is DateTime ru
                              && meta.RemoteUpdateTime is DateTime mu
                              && ru > mu;
            if (!dirty && !meta.IsFolder && (sizeDiffers || updateNewer))
                changes.Add(new RemoteChange.Updated(meta, remote.Entry));
        }

        foreach (var located in remoteLocated)
        {
            if (allStoreRemoteIds.Contains(located.Entry.Id)) continue;
            changes.Add(new RemoteChange.Added(located.Entry, SyncPathUtil.Normalize(located.RelativePath)));
        }
        return changes;
    }
}
