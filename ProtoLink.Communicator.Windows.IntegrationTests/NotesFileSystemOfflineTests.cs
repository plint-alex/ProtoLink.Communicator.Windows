using System.Text;
using ProtoLink.Communicator.Windows.Services;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class NotesFileSystemOfflineTests
{
    [Fact]
    public void BuildTree_ListsNoteFoldersWithIndexHtml()
    {
        var root = CreateTempNotesRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Known bugs"));
            File.WriteAllText(Path.Combine(root, "Known bugs", "index.html"), "<h1>Known bugs</h1><p>body</p>", Encoding.UTF8);
            Directory.CreateDirectory(Path.Combine(root, "Empty child"));
            Directory.CreateDirectory(Path.Combine(root, "Nested"));
            Directory.CreateDirectory(Path.Combine(root, "Nested", "Child"));
            File.WriteAllText(Path.Combine(root, "Nested", "Child", "index.html"), "<h1>Child</h1>", Encoding.UTF8);

            var tree = new NotesFileSystemService().BuildTree(root);
            Assert.NotNull(tree);
            Assert.Equal(3, tree!.Children.Count);
            Assert.Contains(tree.Children, c => c.Name == "Known bugs");
            Assert.Contains(tree.Children, c => c.Name == "Empty child");
            var nested = Assert.Single(tree.Children, c => c.Name == "Nested");
            Assert.Contains(nested.Children, c => c.Name == "Child");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task OpenAndSave_IndexHtml_RoundTripOffline()
    {
        var root = CreateTempNotesRoot();
        try
        {
            var folder = Path.Combine(root, "Daily");
            Directory.CreateDirectory(folder);
            var index = Path.Combine(folder, "index.html");
            File.WriteAllText(index, "<h1>Daily</h1><p>one</p>", Encoding.UTF8);

            var fs = new NotesFileSystemService();
            var files = new NotesFileService();
            Assert.True(fs.HasIndexFile(folder));

            var opened = await files.OpenFileAsync(fs.GetIndexFilePath(folder));
            Assert.Contains("one", opened);

            await files.SaveFileAsync(fs.GetWriteIndexFilePath(folder), "<h1>Daily</h1><p>two</p>");
            var again = await files.OpenFileAsync(fs.GetIndexFilePath(folder));
            Assert.Contains("two", again);
            Assert.DoesNotContain("one", again);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void BuildTree_DesktopMyNotesFolder_WhenPresent()
    {
        const string desktopRoot = @"D:\tmp\MyNotesFolder";
        if (!Directory.Exists(desktopRoot))
            return;

        var tree = new NotesFileSystemService().BuildTree(desktopRoot);
        Assert.NotNull(tree);
        Assert.True(tree!.Children.Count >= 1, "Expected note folders under desktop Notes root");
        Assert.Contains(tree.Children, c =>
            File.Exists(Path.Combine(c.FullPath, "index.html"))
            || File.Exists(Path.Combine(c.FullPath, "index.htm")));
    }

    private static string CreateTempNotesRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ProtoLinkNotesOffline_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
