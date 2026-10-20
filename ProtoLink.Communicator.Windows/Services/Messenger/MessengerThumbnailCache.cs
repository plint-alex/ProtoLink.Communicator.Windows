using System.Collections.Generic;

namespace ProtoLink.Communicator.Windows.Services.Messenger;

/// <summary>
/// In-memory LRU thumbnail cache (cycle 1 — no disk). Keyed by entity id + requested size.
/// Bounded ~200 entries / ~64 MB. Cleared on sign-out.
/// </summary>
public sealed class MessengerThumbnailCache
{
    public const int MaxEntries = 200;
    public const long MaxBytes = 64L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly LinkedList<CacheKey> _order = new();
    private readonly Dictionary<CacheKey, Entry> _map = new();
    private long _totalBytes;

    public static MessengerThumbnailCache Shared { get; } = new();

    public bool TryGet(Guid entityId, int width, int height, out byte[]? bytes)
    {
        var key = new CacheKey(entityId, width, height);
        lock (_gate)
        {
            if (!_map.TryGetValue(key, out var entry))
            {
                bytes = null;
                return false;
            }

            _order.Remove(entry.Node);
            _order.AddFirst(entry.Node);
            bytes = entry.Bytes;
            return true;
        }
    }

    public void Set(Guid entityId, int width, int height, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var key = new CacheKey(entityId, width, height);
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _totalBytes -= existing.Bytes.LongLength;
                _order.Remove(existing.Node);
                _map.Remove(key);
            }

            var node = _order.AddFirst(key);
            _map[key] = new Entry(bytes, node);
            _totalBytes += bytes.LongLength;
            Trim();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _order.Clear();
            _totalBytes = 0;
        }
    }

    public int Count
    {
        get { lock (_gate) return _map.Count; }
    }

    public long TotalBytes
    {
        get { lock (_gate) return _totalBytes; }
    }

    private void Trim()
    {
        while (_map.Count > MaxEntries || _totalBytes > MaxBytes)
        {
            var last = _order.Last;
            if (last == null) break;
            var key = last.Value;
            if (_map.TryGetValue(key, out var entry))
            {
                _totalBytes -= entry.Bytes.LongLength;
                _map.Remove(key);
            }

            _order.RemoveLast();
        }
    }

    private readonly record struct CacheKey(Guid EntityId, int Width, int Height);

    private sealed class Entry
    {
        public Entry(byte[] bytes, LinkedListNode<CacheKey> node)
        {
            Bytes = bytes;
            Node = node;
        }

        public byte[] Bytes { get; }
        public LinkedListNode<CacheKey> Node { get; }
    }
}
