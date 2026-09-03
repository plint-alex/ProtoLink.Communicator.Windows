using System.IO;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Services;

public class SyncMappingStore : ISyncMappingStore
{
    private readonly string _filePath;
    private readonly ILogger<SyncMappingStore> _logger;

    public SyncMappingStore(ILogger<SyncMappingStore> logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "ProtoLinkCommunicator");
        Directory.CreateDirectory(appFolder);
        _filePath = Path.Combine(appFolder, "cloud-sync-mappings.json");
    }

    public IReadOnlyList<CloudSyncMapping> Load()
    {
        if (!File.Exists(_filePath)) return new List<CloudSyncMapping>();
        try
        {
            var json = File.ReadAllText(_filePath);
            var list = JsonConvert.DeserializeObject<List<CloudSyncMapping>>(json);
            return list ?? new List<CloudSyncMapping>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load sync mappings");
            return new List<CloudSyncMapping>();
        }
    }

    public void Save(IEnumerable<CloudSyncMapping> mappings)
    {
        var list = mappings.ToList();
        var json = JsonConvert.SerializeObject(list, Formatting.Indented);
        File.WriteAllText(_filePath, json);
    }
}
