using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// AT-S48 regression: Notes Share sheet must not be a WPF Border sibling of WebView2 —
/// HWND airspace paints over it so Share… looks like a no-op. Host in Popup (own HWND),
/// same pattern as Folder files (AT-N7).
/// </summary>
[Trait("Category", "Offline")]
public class NotesShareSheetAirspaceTests
{
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

    [Fact]
    public void NotesView_ShareSheet_HostedInPopup_NotBorderOverlay()
    {
        var path = Path.Combine(RepoRoot(), "ProtoLink.Communicator.Windows", "Views", "NotesView.xaml");
        var text = File.ReadAllText(path);

        Assert.Contains("NotesShareSheetPopup", text, StringComparison.Ordinal);
        Assert.Contains("<Popup x:Name=\"NotesShareSheetPopup\"", text, StringComparison.Ordinal);
        Assert.Contains("ShareSheetView", text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Visibility=\"{Binding ShareSheet.IsOpen, Converter={StaticResource BooleanToVisibilityConverter}}\"",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NotesViewCode_ApplyShareSheetPopup_DocumentsAirspace()
    {
        var path = Path.Combine(RepoRoot(), "ProtoLink.Communicator.Windows", "Views", "NotesView.xaml.cs");
        var text = File.ReadAllText(path);
        Assert.Contains("ApplyShareSheetPopup", text, StringComparison.Ordinal);
        Assert.Contains("WebView2 airspace", text, StringComparison.Ordinal);
    }
}
