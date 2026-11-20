using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace ProtoLink.Communicator.Windows.Services.Sync;

public sealed class JsonSyncMetadataStore
{
    private readonly string _filePath;
    private readonly object _gate = new();
    private Dictionary<string, SyncItemMeta> _items = new(StringComparer.Ordinal);
    private Dictionary<string, DateTime> _lastSync = new(StringComparer.Ordinal);
    private bool _loaded;
    private int _batchDepth;
    private bool _dirty;

    private sealed class FileDto
    {
        public List<SyncItemMeta> Items { get; set; } = new();
        public Dictionary<string, DateTime> LastSync { get; set; } = new();
    }

    public JsonSyncMetadataStore(string? appDataDir = null)
    {
        var dir = appDataDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProtoLinkCommunicator");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "cloud-sync-metadata.json");
        // Defer Load() until first read/write — MainWindow ctor must not parse multi‑MB meta before Show (P0 visible window).
    }

    private static string Key(string mappingId, string path) =>
        $"{mappingId}|{SyncPathUtil.Normalize(path)}";

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_gate)
        {
            if (_loaded) return;
            LoadUnlocked();
            _loaded = true;
        }
    }

    private void LoadUnlocked()
    {
        if (!File.Exists(_filePath)) return;

        Exception? lastIo = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                string text;
                using (var fs = new FileStream(
                           _filePath,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(fs))
                    text = reader.ReadToEnd();

                var dto = JsonSerializer.Deserialize<FileDto>(text);
                if (dto == null) return;
                _items = dto.Items.ToDictionary(i => Key(i.MappingId, i.RelativePath), i => i);
                _lastSync = dto.LastSync ?? new Dictionary<string, DateTime>();
                return;
            }
            catch (JsonException ex)
            {
                throw new SyncException(
                    $"Corrupt sync metadata at '{_filePath}'. Fix or delete the file, then use Force Download.",
                    inner: ex);
            }
            catch (IOException ex)
            {
                // Sharing violation / transient lock — not corrupt. Soft-fail empty so Notes tree
                // paint and cold start are not aborted (NFR-SYNC-1 / silent-window P0).
                lastIo = ex;
                Thread.Sleep(20 * (attempt + 1));
            }
        }

        // Locked after retries: keep empty in-memory state; next SyncEngine touch can reload.
        _ = lastIo;
    }

    private void SaveUnlocked()
    {
        var dto = new FileDto
        {
            Items = _items.Values.ToList(),
            LastSync = _lastSync
        };
        // Compact JSON: indented 27k-item MyNotesFolder meta is ~13MB and ~1s+ per rewrite.
        var json = JsonSerializer.Serialize(dto);
        Exception? lastIo = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var fs = new FileStream(
                    _filePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read);
                using var writer = new StreamWriter(fs);
                writer.Write(json);
                return;
            }
            catch (IOException ex)
            {
                lastIo = ex;
                Thread.Sleep(20 * (attempt + 1));
            }
        }

        if (lastIo != null)
            throw lastIo;
    }

    private void MarkDirtyOrSaveUnlocked()
    {
        if (_batchDepth > 0)
        {
            _dirty = true;
            return;
        }

        SaveUnlocked();
        _dirty = false;
    }

    /// <summary>
    /// Coalesce many Upsert/Delete into one disk write. Nested batches are supported.
    /// Required for mapped MyNotesFolder (~10k+ items): per-item full-file rewrite never
    /// reaches <see cref="SetLastSyncUtc"/> within a daily-use window (sticky Syncing).
    /// </summary>
    public void BeginBatch()
    {
        EnsureLoaded();
        lock (_gate)
            _batchDepth++;
    }

    public void EndBatch()
    {
        lock (_gate)
        {
            if (_batchDepth <= 0)
                throw new InvalidOperationException("EndBatch without BeginBatch");
            _batchDepth--;
            if (_batchDepth == 0 && _dirty)
            {
                SaveUnlocked();
                _dirty = false;
            }
        }
    }

    /// <summary>Run <paramref name="work"/> inside a metadata batch (one save on success path).</summary>
    public void RunInBatch(Action work)
    {
        BeginBatch();
        try
        {
            work();
        }
        finally
        {
            EndBatch();
        }
    }

    public async Task RunInBatchAsync(Func<Task> work)
    {
        BeginBatch();
        try
        {
            await work().ConfigureAwait(false);
        }
        finally
        {
            EndBatch();
        }
    }

    /// <summary>Test observation: deferred disk write pending inside an open batch.</summary>
    internal bool IsDirtyForTests
    {
        get { lock (_gate) return _dirty; }
    }

    /// <summary>Test observation: nested batch depth.</summary>
    internal int BatchDepthForTests
    {
        get { lock (_gate) return _batchDepth; }
    }

    public List<SyncItemMeta> GetAll(string mappingId)
    {
        EnsureLoaded();
        lock (_gate)
            return _items.Values.Where(i => i.MappingId == mappingId).Select(Clone).ToList();
    }

    /// <summary>
    /// Folder RemoteIds keyed as <c>mappingId|normalizedRelativePath</c> (same key space as internal storage).
    /// One pass — Notes tree share glyphs must not call <see cref="GetAll"/> per node.
    /// </summary>
    public void CopyFolderRemoteIndex(IDictionary<string, Guid> into)
    {
        EnsureLoaded();
        lock (_gate)
        {
            foreach (var i in _items.Values)
            {
                if (!i.IsFolder || i.RemoteId == Guid.Empty)
                    continue;
                into[Key(i.MappingId, i.RelativePath)] = i.RemoteId;
            }
        }
    }

    public HashSet<Guid> GetAllRemoteIds()
    {
        EnsureLoaded();
        lock (_gate)
            return _items.Values.Select(i => i.RemoteId).ToHashSet();
    }

    public SyncItemMeta? FindByRemoteId(Guid remoteId)
    {
        EnsureLoaded();
        lock (_gate)
            return _items.Values.FirstOrDefault(i => i.RemoteId == remoteId) is { } m ? Clone(m) : null;
    }

    /// <summary>Direct children of <paramref name="parentRemoteId"/> (any mapping) for Soft Ready listing.</summary>
    public List<SyncItemMeta> FindChildrenByParentRemoteId(Guid parentRemoteId)
    {
        if (parentRemoteId == Guid.Empty) return new List<SyncItemMeta>();
        EnsureLoaded();
        lock (_gate)
        {
            return _items.Values
                .Where(i => i.ParentRemoteId == parentRemoteId && i.RemoteId != Guid.Empty)
                .Select(Clone)
                .ToList();
        }
    }

    public void Upsert(SyncItemMeta item)
    {
        EnsureLoaded();
        lock (_gate)
        {
            item.RelativePath = SyncPathUtil.Normalize(item.RelativePath);
            _items[Key(item.MappingId, item.RelativePath)] = Clone(item);
            MarkDirtyOrSaveUnlocked();
        }
    }

    public void Delete(string mappingId, string relativePath)
    {
        EnsureLoaded();
        lock (_gate)
        {
            _items.Remove(Key(mappingId, relativePath));
            MarkDirtyOrSaveUnlocked();
        }
    }

    /// <summary>
    /// Drop sync-meta rows whose <see cref="SyncItemMeta.RemoteId"/> is in
    /// <paramref name="remoteIds"/>. Used after Cloud delete so Soft Ready cannot resurrect
    /// soft-hidden entities when GetEntities times out (E10 / AT-CFD Win regress).
    /// </summary>
    public int RemoveByRemoteIds(IEnumerable<Guid> remoteIds)
    {
        var set = remoteIds as HashSet<Guid> ?? remoteIds.ToHashSet();
        if (set.Count == 0) return 0;
        EnsureLoaded();
        lock (_gate)
        {
            var keys = _items
                .Where(kv => set.Contains(kv.Value.RemoteId))
                .Select(kv => kv.Key)
                .ToList();
            foreach (var k in keys)
                _items.Remove(k);
            if (keys.Count > 0)
                MarkDirtyOrSaveUnlocked();
            return keys.Count;
        }
    }

    /// <summary>
    /// Drop all meta + LastSync for a mapping id (e.g. after same-named heal remaps CloudFolderId).
    /// </summary>
    public void ClearMapping(string mappingId)
    {
        if (string.IsNullOrWhiteSpace(mappingId)) return;
        EnsureLoaded();
        lock (_gate)
        {
            var keys = _items
                .Where(kv => string.Equals(kv.Value.MappingId, mappingId, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();
            foreach (var k in keys)
                _items.Remove(k);
            _lastSync.Remove(mappingId);
            // Case-insensitive LastSync key variant
            var lastKeys = _lastSync.Keys
                .Where(k => string.Equals(k, mappingId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var k in lastKeys)
                _lastSync.Remove(k);
            MarkDirtyOrSaveUnlocked();
        }
    }

    public void SetLastSyncUtc(string mappingId, DateTime utc)
    {
        EnsureLoaded();
        lock (_gate)
        {
            _lastSync[mappingId] = utc;
            MarkDirtyOrSaveUnlocked();
        }
    }

    private static SyncItemMeta Clone(SyncItemMeta i) => new()
    {
        MappingId = i.MappingId,
        RemoteId = i.RemoteId,
        ParentRemoteId = i.ParentRemoteId,
        RelativePath = i.RelativePath,
        IsFolder = i.IsFolder,
        SizeBytes = i.SizeBytes,
        RemoteUpdateTime = i.RemoteUpdateTime,
        ContentHash = i.ContentHash
    };
}
