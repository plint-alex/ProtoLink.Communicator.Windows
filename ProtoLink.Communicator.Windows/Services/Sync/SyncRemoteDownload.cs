using System.Diagnostics;

namespace ProtoLink.Communicator.Windows.Services.Sync;

/// <summary>
/// Failed / not-found remote download (null from <c>DownloadBytesOrNullAsync</c>) soft-fails
/// per item during mapped Notes/Cloud reconcile — does not abort the sync pass.
/// Empty remote bytes (<c>Length == 0</c>) are legitimate content and are not a failure.
/// </summary>
public static class SyncRemoteDownload
{
    public static void LogFailed(string phase, string relativePath, Guid remoteId)
    {
        Trace.WriteLine(
            $"[SyncEngine] soft-fail remote download ({phase}): relative={relativePath} remoteId={remoteId}");
    }
}
