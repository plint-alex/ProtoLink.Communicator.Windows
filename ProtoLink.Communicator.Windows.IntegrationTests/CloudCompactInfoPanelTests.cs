using System.Text.RegularExpressions;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// CIP1 — Cloud Info pane is facts-only; actions on list/tree context menus; ⋮ Locations-only.
/// </summary>
[Trait("Category", "Offline")]
public class CloudCompactInfoPanelTests
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
    public void CloudView_FolderPane_HasNo_InfoHeading_Or_ActionChrome()
    {
        var xaml = ReadUnder("ProtoLink.Communicator.Windows", "Views", "CloudView.xaml");
        Assert.DoesNotContain("Text=\"Info\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.Name=\"Share from Info\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.Name=\"Map from Info\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OnMapCurrentFolderClick", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SyncCurrentMappingCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ForceUploadCurrentMappingCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ForceDownloadCurrentMappingCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("UnmapCurrentFolderCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowShare=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowCopyLink=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Entity id\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Synced mapping path\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Shared folder browse-only hint", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudView_FilePreview_Keeps_Open_Download()
    {
        var xaml = ReadUnder("ProtoLink.Communicator.Windows", "Views", "CloudView.xaml");
        var filePane = Regex.Match(
            xaml,
            @"Visibility=""\{Binding IsFileOpen, Converter=\{StaticResource BooleanToVisibilityConverter\}\}"">(.*?)</Grid>\s*<!-- Share side sheet",
            RegexOptions.Singleline);
        Assert.True(filePane.Success, "File preview pane missing");
        Assert.Contains("OpenExternallyCommand", filePane.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("DownloadCurrentCommand", filePane.Groups[1].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudView_CodeBehind_MoreMenu_IsLocationsOnly_And_TreeHasContextMenu()
    {
        var cs = ReadUnder("ProtoLink.Communicator.Windows", "Views", "CloudView.xaml.cs");
        Assert.Contains("CloudLocationsSharedWithMe", cs, StringComparison.Ordinal);
        Assert.Contains("CloudLocationsSharedByMe", cs, StringComparison.Ordinal);
        Assert.Contains("CloudLocationsRecent", cs, StringComparison.Ordinal);
        Assert.Contains("OnFolderTreeContextMenuOpening", cs, StringComparison.Ordinal);
        Assert.Contains("OnFolderTreePreviewRightDown", cs, StringComparison.Ordinal);
        Assert.Contains("FolderTreeRowAsItem", cs, StringComparison.Ordinal);
        Assert.Contains("CreateItemContextMenu", cs, StringComparison.Ordinal);

        // Action items must not be added in RebuildEntityMoreMenu (Locations-only).
        var rebuild = Regex.Match(
            cs,
            @"private void RebuildEntityMoreMenu\(\)\s*\{(?<body>.*?)\n    \}",
            RegexOptions.Singleline);
        Assert.True(rebuild.Success, "RebuildEntityMoreMenu missing");
        var body = rebuild.Groups["body"].Value;
        Assert.DoesNotContain("Header = \"Share…\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Header = \"Delete\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Header = \"Sync\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("DownloadCurrentCommand", body, StringComparison.Ordinal);
        Assert.Contains("CloudLocationsSharedWithMe", body, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudView_Xaml_Wires_FolderTree_ContextMenu()
    {
        var xaml = ReadUnder("ProtoLink.Communicator.Windows", "Views", "CloudView.xaml");
        Assert.Contains("ContextMenuOpening=\"OnFolderTreeContextMenuOpening\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewMouseRightButtonDown=\"OnFolderTreePreviewRightDown\"", xaml, StringComparison.Ordinal);
    }
}
