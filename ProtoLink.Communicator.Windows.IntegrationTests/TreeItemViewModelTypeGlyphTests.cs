using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>E5c / AT-N13: Folder files rows expose CloudFileTypeCue glyphs via TreeItemViewModel.</summary>
[Trait("Category", "Offline")]
public class TreeItemViewModelTypeGlyphTests
{
    [Theory]
    [InlineData("notes.txt", "📄")]
    [InlineData("photo.PNG", "🖼")]
    [InlineData("slide.webp", "🖼")]
    [InlineData("report.pdf", "📄")]
    public void File_TypeGlyph_MatchesCloudFileTypeCue(string name, string expected)
    {
        var item = FileSystemItem.CreateFile(Path.Combine(Path.GetTempPath(), name));
        var vm = new TreeItemViewModel(item);
        Assert.Equal(name, vm.Name);
        Assert.Equal(expected, vm.TypeGlyph);
    }

    [Fact]
    public void Folder_TypeGlyph_IsFolderCue()
    {
        var item = FileSystemItem.CreateFolder(Path.Combine(Path.GetTempPath(), "ChildNote"));
        var vm = new TreeItemViewModel(item);
        Assert.Equal("📁", vm.TypeGlyph);
    }
}
