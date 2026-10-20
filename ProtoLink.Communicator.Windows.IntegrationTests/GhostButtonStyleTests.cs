using System.Text.RegularExpressions;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Guards startup XamlParseException: GhostButton TargetType=Button cannot apply to ToggleButton
/// (ShareSheet expiry chips). TargetType must be ButtonBase (common base of Button and ToggleButton).
/// </summary>
[Trait("Category", "Offline")]
public class GhostButtonStyleTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var theme = Path.Combine(dir.FullName, "ProtoLink.Communicator.Windows", "Themes", "Theme.Dark.xaml");
            if (File.Exists(theme))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate Communicator.Windows repo root from " + AppContext.BaseDirectory);
    }

    [Theory]
    [InlineData("Theme.Dark.xaml")]
    [InlineData("Theme.Light.xaml")]
    public void GhostButton_TargetType_IsButtonBase(string themeFile)
    {
        var path = Path.Combine(RepoRoot(), "ProtoLink.Communicator.Windows", "Themes", themeFile);
        var text = File.ReadAllText(path);
        Assert.Contains("x:Key=\"GhostButton\" TargetType=\"ButtonBase\"", text, StringComparison.Ordinal);
        Assert.False(
            text.Contains("x:Key=\"GhostButton\" TargetType=\"Button\"", StringComparison.Ordinal),
            $"{themeFile}: GhostButton must not use TargetType=Button (breaks ToggleButton).");
    }

    [Fact]
    public void ShareSheet_ExpiryToggleButtons_StillUseGhostButton()
    {
        var path = Path.Combine(RepoRoot(), "ProtoLink.Communicator.Windows", "Views", "ShareSheetView.xaml");
        var text = File.ReadAllText(path);
        var toggleWithGhost = Regex.Matches(
            text,
            @"<ToggleButton[^>]*Style=""\{DynamicResource GhostButton\}""",
            RegexOptions.Singleline);
        Assert.True(toggleWithGhost.Count >= 4,
            "ShareSheet expiry ToggleButtons must keep GhostButton; themes must support ButtonBase.");
    }
}
