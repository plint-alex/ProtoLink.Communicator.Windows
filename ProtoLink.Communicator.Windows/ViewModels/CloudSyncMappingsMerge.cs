using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.ViewModels;

/// <summary>
/// Keeps in-memory <see cref="CloudSyncMapping"/> collections aligned with disk without wiping UI state.
/// AT-CFD-6: verify harness (and heal/tools) may append dedicated maps to cloud-sync-mappings.json
/// after Communicator start — Delete must see those rows for DeleteLocalMirror + quiet unmap.
/// </summary>
public static class CloudSyncMappingsMerge
{
    /// <summary>
    /// Adds store rows whose <see cref="CloudSyncMapping.CloudFolderId"/> is not already present.
    /// Does not remove or overwrite existing in-memory rows (MyNotesFolder parent map stays).
    /// </summary>
    /// <returns>Number of mappings added.</returns>
    public static int AddMissing(
        ObservableCollection<CloudSyncMapping> target,
        IEnumerable<CloudSyncMapping> fromStore)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(fromStore);

        var known = new HashSet<Guid>(target.Select(m => m.CloudFolderId));
        var added = 0;
        foreach (var m in fromStore)
        {
            if (m.CloudFolderId == Guid.Empty) continue;
            if (string.IsNullOrWhiteSpace(m.LocalPath)) continue;
            if (!known.Add(m.CloudFolderId)) continue;
            target.Add(m);
            added++;
        }

        return added;
    }

    /// <summary>
    /// AT-CFD-6: deferred inject may rewrite <see cref="CloudSyncMapping.LocalPath"/> for an id
    /// already present in memory (stale/empty). Refresh path/name from store, then AddMissing.
    /// </summary>
    /// <returns>Number of rows whose LocalPath was refreshed plus newly added rows.</returns>
    public static int UnionFromStore(
        ObservableCollection<CloudSyncMapping> target,
        IEnumerable<CloudSyncMapping> fromStore)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(fromStore);

        var storeRows = fromStore
            .Where(m => m.CloudFolderId != Guid.Empty && !string.IsNullOrWhiteSpace(m.LocalPath))
            .GroupBy(m => m.CloudFolderId)
            .ToDictionary(g => g.Key, g => g.Last());

        var refreshed = 0;
        foreach (var live in target)
        {
            if (!storeRows.TryGetValue(live.CloudFolderId, out var store)) continue;
            var pathChanged = !string.Equals(
                live.LocalPath?.Trim(),
                store.LocalPath.Trim(),
                StringComparison.OrdinalIgnoreCase);
            if (!pathChanged && !string.IsNullOrWhiteSpace(live.LocalPath)) continue;
            live.LocalPath = store.LocalPath;
            if (!string.IsNullOrWhiteSpace(store.CloudFolderName))
                live.CloudFolderName = store.CloudFolderName;
            refreshed++;
        }

        return refreshed + AddMissing(target, storeRows.Values);
    }
}
