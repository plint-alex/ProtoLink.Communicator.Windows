using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ProtoLink.Communicator.Windows.Services;

public class NotesFileService
{
    public async Task<string> OpenFileAsync(string filePath)
    {
        return await File.ReadAllTextAsync(filePath, Encoding.UTF8);
    }

    public async Task SaveFileAsync(string filePath, string content)
    {
        await File.WriteAllTextAsync(filePath, content ?? string.Empty, Encoding.UTF8);
    }
}
