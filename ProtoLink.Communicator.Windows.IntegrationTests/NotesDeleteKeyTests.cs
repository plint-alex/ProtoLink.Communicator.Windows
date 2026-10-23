using System.Windows.Input;
using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class NotesDeleteKeyTests
{
    [Theory]
    [InlineData(Key.Delete, true, false, true)]
    [InlineData(Key.Back, true, false, true)]
    [InlineData(Key.Delete, false, false, false)]
    [InlineData(Key.Back, false, false, false)]
    [InlineData(Key.Delete, true, true, false)]
    [InlineData(Key.Back, true, true, false)]
    [InlineData(Key.Delete, false, true, false)]
    [InlineData(Key.A, true, false, false)]
    public void ShouldHandlePageDeleteKey_AT_DK_tree_vs_editor(
        Key key,
        bool treeFocused,
        bool editorFocused,
        bool expected)
    {
        Assert.Equal(
            expected,
            NotesDeleteKey.ShouldHandlePageDeleteKey(key, treeFocused, editorFocused));
    }

    [Fact]
    public void IsDeletablePagePath_RejectsRoot_AT_DK_3()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-notes-dk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.False(NotesDeleteKey.IsDeletablePagePath(root, root));
            var child = Path.Combine(root, "Child");
            Directory.CreateDirectory(child);
            Assert.True(NotesDeleteKey.IsDeletablePagePath(child, root));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void IsDeletablePagePath_RejectsEmpty()
    {
        Assert.False(NotesDeleteKey.IsDeletablePagePath(null, @"C:\notes"));
        Assert.False(NotesDeleteKey.IsDeletablePagePath("", @"C:\notes"));
    }
}
