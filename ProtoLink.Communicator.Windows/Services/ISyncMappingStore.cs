namespace ProtoLink.Communicator.Windows.Services;

public interface ISyncMappingStore
{
    IReadOnlyList<Models.CloudSyncMapping> Load();
    void Save(IEnumerable<Models.CloudSyncMapping> mappings);
}
