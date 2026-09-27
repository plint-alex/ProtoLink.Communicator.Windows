using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>Local per-contact last-opened watermarks for unread badges.</summary>
public sealed class MessengerReadStore
{
    private readonly string _path;
    private readonly ConcurrentDictionary<string, long> _openedUtcTicks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _io = new();

    public MessengerReadStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProtoLinkCommunicator");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "messenger-read.json");
        Load();
    }

    public DateTime GetOpenedUtc(Guid contactId)
    {
        if (_openedUtcTicks.TryGetValue(contactId.ToString(), out var ticks) && ticks > 0)
            return new DateTime(ticks, DateTimeKind.Utc);
        return DateTime.MinValue;
    }

    public void MarkOpened(Guid contactId, DateTime utcNow)
    {
        var ticks = utcNow.Kind == DateTimeKind.Utc ? utcNow.Ticks : utcNow.ToUniversalTime().Ticks;
        _openedUtcTicks[contactId.ToString()] = ticks;
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var map = JsonSerializer.Deserialize<Dictionary<string, long>>(json);
            if (map == null) return;
            foreach (var kv in map)
                _openedUtcTicks[kv.Key] = kv.Value;
        }
        catch { /* ignore corrupt store */ }
    }

    private void Save()
    {
        try
        {
            lock (_io)
            {
                var snapshot = _openedUtcTicks.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                File.WriteAllText(_path, JsonSerializer.Serialize(snapshot));
            }
        }
        catch { /* non-fatal */ }
    }
}
