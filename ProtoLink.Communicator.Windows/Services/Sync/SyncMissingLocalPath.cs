using System.Diagnostics;
using System.IO;

namespace ProtoLink.Communicator.Windows.Services.Sync;

/// <summary>
/// NFR-SYNC-1: mapped Notes/Cloud item whose local path vanished mid-pass
/// (FileNotFound / DirectoryNotFound) must soft-fail per item — not abort the sync.
/// </summary>
public static class SyncMissingLocalPath
{
    public static bool IsMissing(Exception ex) =>
        ex is FileNotFoundException or DirectoryNotFoundException;

    public static void Log(string phase, string relativePath, string fullPath, Exception? ex = null)
    {
        var detail = ex == null
            ? "path gone"
            : $"{ex.GetType().Name}: {ex.Message}";
        Trace.WriteLine(
            $"[SyncEngine] soft-fail missing local ({phase}): relative={relativePath} full={fullPath} — {detail}");
    }
}
