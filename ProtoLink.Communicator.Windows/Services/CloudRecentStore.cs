using System.IO;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// LocalAppData MRU of opened Cloud entities (<c>cloud-recent.json</c>).
/// </summary>
public sealed class CloudRecentStore : ICloudRecentStore
{
    private readonly string _path;
    private readonly object _io = new();
    private List<CloudRecentEntry> _entries = new();

    public CloudRecentStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProtoLinkCommunicator"))
    {
    }

    /// <summary>Test seam: store under a specific app-data directory.</summary>
    public CloudRecentStore(string appDataDir)
    {
        Directory.CreateDirectory(appDataDir);
        _path = Path.Combine(appDataDir, "cloud-recent.json");
        LoadFromDisk();
    }

    public IReadOnlyList<CloudRecentEntry> Load()
    {
        lock (_io)
            return _entries.ToList();
    }

    public IReadOnlyList<CloudRecentEntry> RecordOpen(CloudRecentEntry entry)
    {
        lock (_io)
        {
            _entries = CloudRecentList.Push(_entries, entry).ToList();
            SaveUnlocked();
            return _entries.ToList();
        }
    }

    public IReadOnlyList<CloudRecentEntry> Remove(Guid entityId)
    {
        lock (_io)
        {
            _entries = CloudRecentList.Remove(_entries, entityId).ToList();
            SaveUnlocked();
            return _entries.ToList();
        }
    }

    public IReadOnlyList<CloudRecentEntry> Replace(IReadOnlyList<CloudRecentEntry> entries)
    {
        lock (_io)
        {
            _entries = entries
                .Where(e => e.EntityId != Guid.Empty && !CloudVirtualNodes.IsVirtual(e.EntityId))
                .Take(CloudRecentList.DefaultCapacity)
                .ToList();
            SaveUnlocked();
            return _entries.ToList();
        }
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var rows = JsonSerializer.Deserialize<List<Dto>>(json);
            if (rows == null) return;
            _entries = rows
                .Where(r => r.EntityId != Guid.Empty)
                .Select(r => new CloudRecentEntry(
                    r.EntityId,
                    string.IsNullOrWhiteSpace(r.Name) ? r.EntityId.ToString("D") : r.Name,
                    r.IsFolder,
                    r.OpenedUtc.Kind == DateTimeKind.Utc ? r.OpenedUtc : r.OpenedUtc.ToUniversalTime()))
                .Where(e => !CloudVirtualNodes.IsVirtual(e.EntityId))
                .Take(CloudRecentList.DefaultCapacity)
                .ToList();
        }
        catch
        {
            _entries = new List<CloudRecentEntry>();
        }
    }

    private void SaveUnlocked()
    {
        try
        {
            var dto = _entries.Select(e => new Dto
            {
                EntityId = e.EntityId,
                Name = e.Name,
                IsFolder = e.IsFolder,
                OpenedUtc = e.OpenedUtc.Kind == DateTimeKind.Utc ? e.OpenedUtc : e.OpenedUtc.ToUniversalTime()
            }).ToList();
            File.WriteAllText(_path, JsonSerializer.Serialize(dto));
        }
        catch
        {
            // Persistence is best-effort; navigation must not fail.
        }
    }

    private sealed class Dto
    {
        public Guid EntityId { get; set; }
        public string Name { get; set; } = "";
        public bool IsFolder { get; set; }
        public DateTime OpenedUtc { get; set; }
    }
}
