using ProtoLink.Communicator.Windows.Services.Sync;
using Xunit;
using Xunit.Abstractions;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Offline: seed empty ContentHash from local files so same-size edits sync without a full remote walk.</summary>
public class SeedContentHashesFromLocalTests
{
    private readonly ITestOutputHelper _output;
    public SeedContentHashesFromLocalTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Seed_EmptyContentHashes_FromLocalNotesFiles()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProtoLinkCommunicator");
        var store = new JsonSyncMetadataStore(appData);
        var settingsPath = Path.Combine(appData, "settings.json");
        var notesRoot = System.Text.Json.JsonSerializer.Deserialize<Models.AppSettings>(
            File.ReadAllText(settingsPath), IntegrationTestHttp.JsonOptions)!.NotesRootPath!;
        var mappingId = Guid.Parse("bcd7a62e-42a8-4cb9-95a7-7893b4c92752").ToString("N");

        var seeded = 0;
        foreach (var meta in store.GetAll(mappingId).Where(m => !m.IsFolder && string.IsNullOrEmpty(m.ContentHash)))
        {
            var full = Path.Combine(notesRoot, meta.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            // Case-insensitive resolve on Windows
            if (!File.Exists(full))
            {
                var dir = Path.GetDirectoryName(full)!;
                var name = Path.GetFileName(full);
                if (Directory.Exists(dir))
                {
                    var match = Directory.GetFiles(dir).FirstOrDefault(f =>
                        string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
                    if (match != null) full = match;
                }
            }
            if (!File.Exists(full)) continue;
            meta.ContentHash = ContentHashUtil.Sha256HexFile(full);
            meta.SizeBytes = new FileInfo(full).Length;
            store.Upsert(meta);
            seeded++;
        }
        _output.WriteLine($"Seeded {seeded} ContentHash values");
        var empty = store.GetAll(mappingId).Count(m => !m.IsFolder && string.IsNullOrEmpty(m.ContentHash));
        _output.WriteLine($"Remaining empty={empty}");
        Assert.True(seeded > 0 || empty == 0);
    }
}
