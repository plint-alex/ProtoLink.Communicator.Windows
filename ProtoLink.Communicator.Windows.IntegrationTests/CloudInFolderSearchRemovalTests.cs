using System.Text.RegularExpressions;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// CIF1 — Cloud list pane must not expose in-folder search; listing binds unfiltered Items.
/// </summary>
[Trait("Category", "Offline")]
public class CloudInFolderSearchRemovalTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var view = Path.Combine(dir.FullName, "ProtoLink.Communicator.Windows", "Views", "CloudView.xaml");
            if (File.Exists(view))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate Communicator.Windows repo root from " + AppContext.BaseDirectory);
    }

    private static string CloudViewXaml() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "ProtoLink.Communicator.Windows", "Views", "CloudView.xaml"));

    private static string CloudViewCodeBehind() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "ProtoLink.Communicator.Windows", "Views", "CloudView.xaml.cs"));

    private static string CloudViewModelSource() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "ProtoLink.Communicator.Windows", "ViewModels", "CloudViewModel.cs"));

    [Fact]
    public void CloudView_HasNo_ListSearchBox_Or_SearchInThisFolder_AutomationName()
    {
        var xaml = CloudViewXaml();
        Assert.DoesNotContain("ListSearchBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Search in this folder", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchText", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudListBox_Binds_Items_Not_FilteredItems()
    {
        var xaml = CloudViewXaml();
        var m = Regex.Match(
            xaml,
            @"x:Name=""CloudListBox""[^>]*ItemsSource=""\{Binding ([^}]+)\}""",
            RegexOptions.Singleline);
        Assert.True(m.Success, "CloudListBox ItemsSource binding missing");
        Assert.Equal("Items", m.Groups[1].Value);
        Assert.DoesNotContain("FilteredItems", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudView_ListHeader_NoLonger_Hosts_Refresh_Or_AddLocal_After_Carg1()
    {
        var xaml = CloudViewXaml();
        Assert.DoesNotContain("AutomationProperties.Name=\"Refresh Cloud listing\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Refresh\"", xaml, StringComparison.Ordinal);
        // FAB context menu may still expose AddLocalFolderCommand; list/empty header must not.
        Assert.Contains("Command=\"{Binding AddLocalFolderCommand}\"", xaml, StringComparison.Ordinal);
        var headerStart = xaml.IndexOf("CM-HD2", StringComparison.Ordinal);
        Assert.True(headerStart >= 0, "CM-HD2 header marker missing");
        var headerEnd = xaml.IndexOf("Offline / status banners", headerStart, StringComparison.Ordinal);
        Assert.True(headerEnd > headerStart, "header/status boundary missing");
        var header = xaml[headerStart..headerEnd];
        Assert.DoesNotContain("AddLocalFolderCommand", header, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshCommand", header, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudView_PreviewKeyDown_HasNo_CtrlF_SearchFocus()
    {
        var cs = CloudViewCodeBehind();
        Assert.DoesNotContain("ListSearchBox", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("Key.F && Keyboard.Modifiers == ModifierKeys.Control", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudViewModel_HasNo_SearchFilter_Path()
    {
        var cs = CloudViewModelSource();
        Assert.DoesNotContain("SearchText", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("FilteredItems", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("MatchesSearchFilter", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("_searchText", cs, StringComparison.Ordinal);
    }
}
