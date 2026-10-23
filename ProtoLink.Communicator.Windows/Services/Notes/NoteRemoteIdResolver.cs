using System.IO;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services.Sync;

namespace ProtoLink.Communicator.Windows.Services.Notes;

/// <summary>
/// E2 note identity: mapped local folder → relative path → folder <see cref="SyncItemMeta.RemoteId"/>.
/// </summary>
public sealed class NoteRemoteIdResolver : INoteRemoteIdResolver
{
    private readonly ISyncMappingStore _syncStore;
    private readonly Func<JsonSyncMetadataStore> _metaStoreFactory;
    private JsonSyncMetadataStore? _metaStore;
    private Dictionary<string, Guid>? _folderRemoteByKey;

    public NoteRemoteIdResolver(
        ISyncMappingStore syncStore,
        Func<JsonSyncMetadataStore>? metaStoreFactory = null)
    {
        _syncStore = syncStore;
        _metaStoreFactory = metaStoreFactory ?? (() => new JsonSyncMetadataStore());
    }

    public Guid? Resolve(string noteFolderFullPath)
    {
        if (string.IsNullOrWhiteSpace(noteFolderFullPath))
            return null;

        try
        {
            var mappings = ToMappingInfos(_syncStore.Load());
            if (mappings.Count == 0)
                return null;

            var mapping = FindBestMapping(noteFolderFullPath, mappings);
            if (mapping == null)
                return null;

            // One store + one folder index per resolver — never GetAll()/clone per tree glyph (cold-start P0).
            EnsureFolderIndex();
            string relative;
            try
            {
                relative = Path.GetRelativePath(mapping.LocalRootPath, Path.GetFullPath(noteFolderFullPath));
            }
            catch
            {
                return null;
            }

            if (relative.StartsWith("..", StringComparison.Ordinal))
                return null;

            var normalized = SyncPathUtil.Normalize(relative);
            if (normalized is "." or "")
                normalized = "";

            var key = $"{mapping.Id}|{normalized}";
            if (_folderRemoteByKey != null
                && _folderRemoteByKey.TryGetValue(key, out var remoteId)
                && remoteId != Guid.Empty)
                return remoteId;
            return null;
        }
        catch (SyncException)
        {
            // Corrupt meta must not abort Notes tree paint / cold start.
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void EnsureFolderIndex()
    {
        if (_folderRemoteByKey != null)
            return;
        _metaStore ??= _metaStoreFactory();
        var index = new Dictionary<string, Guid>(StringComparer.Ordinal);
        _metaStore.CopyFolderRemoteIndex(index);
        _folderRemoteByKey = index;
    }

    public Guid? ResolveFrom(
        string noteFolderFullPath,
        IReadOnlyList<SyncMappingInfo> mappings,
        IReadOnlyList<SyncItemMeta> items)
    {
        if (string.IsNullOrWhiteSpace(noteFolderFullPath) || mappings == null || mappings.Count == 0)
            return null;

        var mapping = FindBestMapping(noteFolderFullPath, mappings);
        if (mapping == null)
            return null;

        string relative;
        try
        {
            relative = Path.GetRelativePath(mapping.LocalRootPath, Path.GetFullPath(noteFolderFullPath));
        }
        catch
        {
            return null;
        }

        if (relative.StartsWith("..", StringComparison.Ordinal))
            return null;

        var normalized = SyncPathUtil.Normalize(relative);
        // GetRelativePath returns "." when note is the mapping root; meta uses "" for that key space.
        if (normalized is "." or "")
            normalized = "";

        if (items == null || items.Count == 0)
            return null;

        foreach (var item in items)
        {
            if (!item.IsFolder)
                continue;
            if (!string.Equals(item.MappingId, mapping.Id, StringComparison.Ordinal))
                continue;
            if (!string.Equals(SyncPathUtil.Normalize(item.RelativePath), normalized, StringComparison.Ordinal))
                continue;
            return item.RemoteId == Guid.Empty ? null : item.RemoteId;
        }

        return null;
    }

    internal static List<SyncMappingInfo> ToMappingInfos(IEnumerable<CloudSyncMapping> mappings)
    {
        var list = new List<SyncMappingInfo>();
        foreach (var m in mappings)
        {
            if (string.IsNullOrWhiteSpace(m.LocalPath))
                continue;
            try
            {
                list.Add(new SyncMappingInfo
                {
                    Id = m.CloudFolderId.ToString("N"),
                    CloudFolderId = m.CloudFolderId,
                    LocalRootPath = Path.GetFullPath(m.LocalPath),
                    CloudFolderName = m.CloudFolderName ?? ""
                });
            }
            catch
            {
                // Skip invalid paths
            }
        }
        return list;
    }

    private static SyncMappingInfo? FindBestMapping(
        string noteFolderFullPath,
        IReadOnlyList<SyncMappingInfo> mappings)
    {
        string noteFull;
        try
        {
            noteFull = Path.GetFullPath(noteFolderFullPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }

        SyncMappingInfo? best = null;
        var bestLen = -1;
        foreach (var m in mappings)
        {
            if (string.IsNullOrWhiteSpace(m.LocalRootPath))
                continue;
            string rootFull;
            try
            {
                rootFull = Path.GetFullPath(m.LocalRootPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                continue;
            }

            if (!LocalPathCoverage.IsUnderOrEqual(noteFull, rootFull))
                continue;
            if (rootFull.Length > bestLen)
            {
                best = m;
                bestLen = rootFull.Length;
            }
        }

        return best;
    }
}
