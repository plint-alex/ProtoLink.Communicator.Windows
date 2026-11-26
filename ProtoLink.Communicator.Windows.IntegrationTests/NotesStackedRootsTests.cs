using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class NotesStackedRootsTests
{
    [Fact]
    public void FindContainingRoot_PrefersLongestMatch()
    {
        var roots = new[] { @"C:\Notes", @"C:\Notes\Work" };
        Assert.Equal(
            @"C:\Notes\Work",
            NotesStackedRoots.FindContainingRoot(roots, @"C:\Notes\Work\PageA"));
        Assert.Equal(
            @"C:\Notes",
            NotesStackedRoots.FindContainingRoot(roots, @"C:\Notes\Other"));
        Assert.Null(NotesStackedRoots.FindContainingRoot(roots, @"D:\Elsewhere\Page"));
    }

    [Fact]
    public void ResolveLastOpenPage_RequiresExistingPathUnderRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ProtoLinkStack_" + Guid.NewGuid().ToString("N"));
        var page = Path.Combine(root, "Page");
        Directory.CreateDirectory(page);
        try
        {
            Assert.Equal(
                Path.GetFullPath(page),
                NotesStackedRoots.ResolveLastOpenPage(new[] { root }, page));
            Assert.Null(NotesStackedRoots.ResolveLastOpenPage(new[] { root }, Path.Combine(root, "Missing")));
            Assert.Null(NotesStackedRoots.ResolveLastOpenPage(new[] { root }, @"D:\nope"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void AllExistingRootsEmpty_TrueOnlyWhenAllOnDiskEmpty()
    {
        Assert.True(NotesStackedRoots.AllExistingRootsEmpty(new[]
        {
            (@"C:\A", 0, true),
            (@"C:\B", 0, true)
        }));
        Assert.False(NotesStackedRoots.AllExistingRootsEmpty(new[]
        {
            (@"C:\A", 0, true),
            (@"C:\B", 2, true)
        }));
        Assert.False(NotesStackedRoots.AllExistingRootsEmpty(new[]
        {
            (@"C:\Missing", 0, false)
        }));
    }
}
