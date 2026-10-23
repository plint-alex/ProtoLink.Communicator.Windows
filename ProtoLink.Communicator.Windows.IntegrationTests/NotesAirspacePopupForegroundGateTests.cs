using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>AT-N12 / E5b: gate Popup HWND open while Communicator is backgrounded.</summary>
[Trait("Category", "Offline")]
public class NotesAirspacePopupForegroundGateTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void ShouldOpenPopup_GatesDesiredBySuspend(bool desired, bool suspended, bool expected) =>
        Assert.Equal(expected, NotesAirspacePopupForegroundGate.ShouldOpenPopup(desired, suspended));

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void ShouldRestoreFormatBar_RequiresWasOpenAndPage(bool wasOpen, bool hasPage, bool expected) =>
        Assert.Equal(expected, NotesAirspacePopupForegroundGate.ShouldRestoreFormatBar(wasOpen, hasPage));

    [Fact]
    public void NotesView_ExposesSuspendResumeForMainWindow()
    {
        var path = Path.Combine(RepoRoot(), "ProtoLink.Communicator.Windows", "Views", "NotesView.xaml.cs");
        var text = File.ReadAllText(path);
        Assert.Contains("SuspendAirspacePopupsForBackground", text, StringComparison.Ordinal);
        Assert.Contains("ResumeAirspacePopupsForForeground", text, StringComparison.Ordinal);
        Assert.Contains("NotesAirspacePopupForegroundGate", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_SuspendsPopupsOnDeactivated()
    {
        var path = Path.Combine(RepoRoot(), "ProtoLink.Communicator.Windows", "MainWindow.xaml.cs");
        var text = File.ReadAllText(path);
        Assert.Contains("SuspendAirspacePopupsForBackground", text, StringComparison.Ordinal);
        Assert.Contains("ResumeAirspacePopupsForForeground", text, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var view = Path.Combine(dir.FullName, "ProtoLink.Communicator.Windows", "Views", "NotesView.xaml");
            if (File.Exists(view))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate Communicator.Windows repo root from " + AppContext.BaseDirectory);
    }
}
