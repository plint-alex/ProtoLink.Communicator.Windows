namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// Serializes disk access under mapped Notes/Cloud folders so note save and sync
/// never open the same file concurrently (Windows exclusive locks).
/// </summary>
public static class MappedFolderIoGate
{
    public static readonly SemaphoreSlim Mutex = new(1, 1);

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
}
