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
}
