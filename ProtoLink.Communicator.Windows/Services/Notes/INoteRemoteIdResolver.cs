using ProtoLink.Communicator.Windows.Services.Sync;

namespace ProtoLink.Communicator.Windows.Services.Notes;

/// <summary>
/// Decision E2: note folder path → sync mapping → folder <see cref="SyncItemMeta.RemoteId"/>.
/// Null when unmapped or not yet synced. Pure with respect to the supplied snapshots (offline-testable).
/// </summary>
public interface INoteRemoteIdResolver
{
    /// <summary>Resolve using live mapping store + sync metadata on disk.</summary>
    Guid? Resolve(string noteFolderFullPath);

    /// <summary>Pure resolve for unit tests — no I/O.</summary>
    Guid? ResolveFrom(
        string noteFolderFullPath,
        IReadOnlyList<SyncMappingInfo> mappings,
        IReadOnlyList<SyncItemMeta> items);
}
