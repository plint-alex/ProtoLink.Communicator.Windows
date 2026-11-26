using System.IO;

namespace ProtoLink.Communicator.Windows.Services.Sync;

/// <summary>
/// Detects empty / folders-only / near-empty file inventory so Sync seeds inventory from remote
/// instead of treating a collapsed meta as warm (sticky Syncing… / blind AutoPush).
/// Live 1.0.119 cite: 317 folders + 2 files while local MyNotesFolder ~18k files — the
/// strict folders-only gate skipped heal because two residual file rows remained.
/// 1.0.120 still FAIL: budgeted Push→Reconcile cancelled during WalkRemote before any meta flush.
/// </summary>
internal static class SyncMetaInventoryHeal
{
    /// <summary>
    /// True when metadata must be rebuilt via inventory seed / full Reconcile
    /// before push or compare can converge.
    /// </summary>
    public static bool NeedsRemoteBootstrap(
        IReadOnlyList<SyncItemMeta> meta,
        string? localRootPath = null)
    {
        if (meta.Count == 0)
            return true;
        if (meta.All(m => m.IsFolder))
            return true;

        var folders = 0;
        var files = 0;
        foreach (var m in meta)
        {
            if (m.IsFolder) folders++;
            else files++;
        }

        if (!IsNearEmptyFileInventory(folders, files))
            return false;

        if (string.IsNullOrEmpty(localRootPath) || !Directory.Exists(localRootPath))
            return true;

        // Confirm local still has substantially more files than meta (not a true tiny-file tree).
        var localFiles = CountLocalFilesBounded(localRootPath, maxExclusive: files + 16);
        return localFiles > files + 8;
    }

    /// <summary>
    /// Folder-rich inventory with almost no file rows — Force folder-first / interrupted
    /// bootstrap signature (not a healthy warm map like ~176 folders + ~152 files).
    /// </summary>
    internal static bool IsNearEmptyFileInventory(int folders, int files)
    {
        if (folders < 8)
            return false;
        if (files <= 2)
            return true;
        // folders dominate residual file rows (e.g. 317 folders vs a handful of files).
        return files * 16 < folders;
    }

    private static int CountLocalFilesBounded(string root, int maxExclusive)
    {
        var n = 0;
        try
        {
            foreach (var _ in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                n++;
                if (n >= maxExclusive)
                    return n;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Soft-fail: prefer heal when we cannot count (same class as empty-meta caution).
            return maxExclusive;
        }

        return n;
    }
}
