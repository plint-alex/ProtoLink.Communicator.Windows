using System.Text;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class NotesSiblingFilesTests
{
    [Fact]
    public void BuildTree_ExcludesIndexNames_IncludesFilesAndNested()
    {
        var root = CreateTempFolder();
        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<h1>Note</h1>", Encoding.UTF8);
            File.WriteAllText(Path.Combine(root, "Index.HTM"), "<h1>Alt</h1>", Encoding.UTF8);
            File.WriteAllText(Path.Combine(root, "a.pdf"), "pdf", Encoding.UTF8);
            var sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "b.txt"), "text", Encoding.UTF8);
            File.WriteAllText(Path.Combine(sub, "index.html"), "<h1>Child</h1>", Encoding.UTF8);

            var tree = NotesSiblingFiles.BuildTree(root);
            Assert.NotNull(tree);
            Assert.DoesNotContain(tree!.Children, c => NotesSiblingFiles.IsNotesOwnedFileName(c.Name));
            Assert.Contains(tree.Children, c => c.Name == "a.pdf" && c.Type == FileSystemItemType.File);
            var nested = Assert.Single(tree.Children, c => c.Name == "sub" && c.Type == FileSystemItemType.Folder);
            Assert.Contains(nested.Children, c => c.Name == "b.txt");
            Assert.DoesNotContain(nested.Children, c => NotesSiblingFiles.IsNotesOwnedFileName(c.Name));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void BuildTree_OnlyOwnedIndex_ReturnsNull()
    {
        var root = CreateTempFolder();
        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<p>x</p>", Encoding.UTF8);
            Assert.Null(NotesSiblingFiles.BuildTree(root));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void BuildTree_MissingFolder_ReturnsNullWithoutThrow()
    {
        var missing = Path.Combine(Path.GetTempPath(), "ProtoLinkNotesSiblingMissing_" + Guid.NewGuid().ToString("N"));
        Assert.Null(NotesSiblingFiles.BuildTree(missing));
        Assert.Null(NotesSiblingFiles.BuildTree(null));
        Assert.Null(NotesSiblingFiles.BuildTree(""));
    }

    [Theory]
    [InlineData("index.html", true)]
    [InlineData("INDEX.HTML", true)]
    [InlineData("index.htm", true)]
    [InlineData("Index.HTM", true)]
    [InlineData("a.pdf", false)]
    [InlineData("index.html.bak", false)]
    public void IsNotesOwnedFileName_CaseInsensitive(string name, bool owned)
    {
        Assert.Equal(owned, NotesSiblingFiles.IsNotesOwnedFileName(name));
    }

    private static string CreateTempFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "ProtoLinkNotesSibling_" + Guid.NewGuid().ToString("N"));
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
