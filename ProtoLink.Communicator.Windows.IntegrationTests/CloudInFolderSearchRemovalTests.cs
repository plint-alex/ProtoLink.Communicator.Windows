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
    public void CloudView_Refresh_And_AddLocal_Stay_Discoverable_Until_Carg1()
    {
        var xaml = CloudViewXaml();
        Assert.Contains("AutomationProperties.Name=\"Refresh Cloud listing\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AddLocalFolderCommand}\"", xaml, StringComparison.Ordinal);
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
