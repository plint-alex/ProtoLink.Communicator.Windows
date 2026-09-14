using System.IO;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// Serializes writes to files under mapped Notes/Cloud folders so note save and sync
/// never write the same file concurrently (Windows exclusive locks).
/// Only disk operations run under the gate — never network calls — so opening a note
/// never waits for a cloud sync to finish.
/// </summary>
public static class MappedFolderIoGate
{
    /// <summary>Upload sources up to this size are snapshotted to memory so no disk handle stays open during the POST.</summary>
    private const long UploadSnapshotLimitBytes = 8L * 1024 * 1024;

    private static readonly SemaphoreSlim Mutex = new(1, 1);

    public static async Task RunAsync(Func<Task> work)
    {
        await Mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            await work().ConfigureAwait(false);
        }
        finally
        {
            Mutex.Release();
        }
    }

    public static async Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        await Mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            return await work().ConfigureAwait(false);
        }
        finally
        {
            Mutex.Release();
        }
    }

    public static async Task RunSyncAsync(Action work)
    {
        await Mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            work();
        }
        finally
        {
            Mutex.Release();
        }
    }

    public static Task WriteAllBytesAsync(string fullPath, byte[] bytes, CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            EnsureParentDirectory(fullPath);
            await File.WriteAllBytesAsync(fullPath, bytes, ct).ConfigureAwait(false);
        });

    /// <summary>Copy a remote stream onto disk; the gate covers the write only for as long as the copy runs.</summary>
    public static Task WriteStreamAsync(string fullPath, Stream content, CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            EnsureParentDirectory(fullPath);
            await using var fs = File.Create(fullPath);
            await content.CopyToAsync(fs, ct).ConfigureAwait(false);
        });

    public static Task MoveFileAsync(string fromPath, string toPath) =>
        RunSyncAsync(() =>
        {
            EnsureParentDirectory(toPath);
            if (File.Exists(toPath)) File.Delete(toPath);
            File.Move(fromPath, toPath);
        });

    public static Task MoveDirectoryAsync(string fromPath, string toPath) =>
        RunSyncAsync(() =>
        {
            EnsureParentDirectory(toPath);
            if (Directory.Exists(toPath)) Directory.Delete(toPath, recursive: true);
            Directory.Move(fromPath, toPath);
        });

    public static Task DeleteFileAsync(string fullPath) =>
        RunSyncAsync(() => { if (File.Exists(fullPath)) File.Delete(fullPath); });

    public static Task DeleteDirectoryAsync(string fullPath) =>
        RunSyncAsync(() => { if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true); });

    /// <summary>
    /// Content stream for an upload. Small files are copied to memory under the gate so the
    /// network POST holds no file handle; large files stream from disk with shared access.
    /// </summary>
    public static async Task<Stream> OpenUploadStreamAsync(string fullPath, CancellationToken ct = default)
    {
        if (new FileInfo(fullPath).Length <= UploadSnapshotLimitBytes)
        {
            var bytes = await RunAsync(async () =>
                await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false)).ConfigureAwait(false);
            return new MemoryStream(bytes, writable: false);
        }

        return OpenSharedRead(fullPath);
    }

    /// <summary>Read handle that never blocks (or is blocked by) a concurrent note save.</summary>
    public static FileStream OpenSharedRead(string fullPath) =>
        new(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static void EnsureParentDirectory(string fullPath)
    {
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }
}
