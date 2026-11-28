using System.Text.RegularExpressions;
using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// CARG1 — Cloud Refresh + Add-local live beside Settings gear; list header duplicates gone.
/// </summary>
[Trait("Category", "Offline")]
public class CloudAddRefreshNearGearTests
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

    private static string ReadUnder(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    [Fact]
    public void CloudView_ListHeader_HasNo_Refresh_Or_AddLocal_Buttons()
    {
        var xaml = ReadUnder("ProtoLink.Communicator.Windows", "Views", "CloudView.xaml");
        Assert.DoesNotContain("AutomationProperties.Name=\"Refresh Cloud listing\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Refresh\"", xaml, StringComparison.Ordinal);
        // Empty-state Add-local CTA removed; FAB context menu may still bind AddLocalFolderCommand.
        var emptyPanel = Regex.Match(
            xaml,
            @"Visibility=""\{Binding IsEmpty, Converter=\{StaticResource BooleanToVisibilityConverter\}\}"">(.*?)</StackPanel>",
            RegexOptions.Singleline);
        Assert.True(emptyPanel.Success, "Empty-state StackPanel missing");
        Assert.DoesNotContain("AddLocalFolderCommand", emptyPanel.Groups[1].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Wires_CloudChrome_NearGear_AutomationNames()
    {
        var cs = ReadUnder("ProtoLink.Communicator.Windows", "MainWindow.xaml.cs");
        Assert.Contains("CreateCloudChromeButton", cs, StringComparison.Ordinal);
        Assert.Contains("Refresh Cloud listing", cs, StringComparison.Ordinal);
        Assert.Contains("CloudViewModel.AddLocalFolderAutomationName", cs, StringComparison.Ordinal);
        Assert.Contains("CloudViewModel.ShowAddLocalFolderCta", cs, StringComparison.Ordinal);
        Assert.Contains("_cloudViewModel.RefreshCommand", cs, StringComparison.Ordinal);
        Assert.Contains("_cloudViewModel.AddLocalFolderCommand", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellTabToolbarChrome_CloudIds_Match_Carg1()
    {
        Assert.Equal("cloud-refresh", ShellTabToolbarChrome.CloudRefreshActionId);
        Assert.Equal("cloud-add-local", ShellTabToolbarChrome.CloudAddLocalActionId);
        var ids = ShellTabToolbarChrome.PermanentActionIds(ShellTabToolbarChrome.CloudTabIndex);
        Assert.Equal(2, ids.Count);
        Assert.Equal(ShellTabToolbarChrome.CloudRefreshActionId, ids[0]);
        Assert.Equal(ShellTabToolbarChrome.CloudAddLocalActionId, ids[1]);
    }

    [Fact]
    public void CloudView_Keeps_F5_And_Context_Refresh()
    {
        var codeBehind = ReadUnder("ProtoLink.Communicator.Windows", "Views", "CloudView.xaml.cs");
        Assert.Contains("Key.F5", codeBehind, StringComparison.Ordinal);
        Assert.Contains("RefreshCommand", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Header = \"Refresh\"", codeBehind, StringComparison.Ordinal);
    }
}
