using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class WindowsExplorerTests
{
    [Fact]
    public void BuildSelectArguments_UsesSelectAndQuotedFullPath()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pl-explorer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var expected = Path.GetFullPath(folder);
            var args = WindowsExplorer.BuildSelectArguments(folder);
            Assert.Equal("/select,\"" + expected + "\"", args);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void TryOpenFolderSelected_MissingPath_ReturnsFalse()
    {
        var missing = Path.Combine(Path.GetTempPath(), "pl-explorer-missing-" + Guid.NewGuid().ToString("N"));
        Assert.False(WindowsExplorer.TryOpenFolderSelected(missing));
        Assert.False(WindowsExplorer.TryOpenFolderSelected(""));
        Assert.False(WindowsExplorer.TryOpenFolderSelected(null!));
    }
}
