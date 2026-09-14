using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ProtoLink.Communicator.Windows.Services;

public class NotesFileService
{
    private static readonly TimeSpan[] IoRetryDelays =
    {
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(150),
        TimeSpan.FromMilliseconds(400)
    };

    /// <summary>
    /// Reads with shared access and without the write gate: opening a note must never queue
    /// behind a running cloud sync, which left the editor blank until the sync finished.
    /// </summary>
    public async Task<string> OpenFileAsync(string filePath)
    {
        return await ReadAllTextWithRetryAsync(filePath).ConfigureAwait(false);
    }

    public async Task SaveFileAsync(string filePath, string content)
    {
        var text = content ?? string.Empty;
        await MappedFolderIoGate.RunAsync(async () =>
            await WriteAllTextWithRetryAsync(filePath, text).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static async Task<string> ReadAllTextWithRetryAsync(string filePath)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var stream = MappedFolderIoGate.OpenSharedRead(filePath);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            }
            catch (IOException) when (attempt < IoRetryDelays.Length)
            {
                await Task.Delay(IoRetryDelays[attempt]).ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteAllTextWithRetryAsync(string filePath, string text)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await File.WriteAllTextAsync(filePath, text, Encoding.UTF8).ConfigureAwait(false);
                return;
            }
            catch (IOException) when (attempt < IoRetryDelays.Length)
            {
                await Task.Delay(IoRetryDelays[attempt]).ConfigureAwait(false);
            }
        }
    }
}
