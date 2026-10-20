using System.Text.RegularExpressions;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Guards startup InvalidOperationException: TextBox Text defaults to TwoWay, but
/// ShareSheetViewModel.CreatedUrl has a private setter (WPF read-only) → crash at window show.
/// Display-only TextBox must bind Mode=OneWay.
/// </summary>
[Trait("Category", "Offline")]
public class CreatedUrlBindingTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var view = Path.Combine(dir.FullName, "ProtoLink.Communicator.Windows", "Views", "ShareSheetView.xaml");
            if (File.Exists(view))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate Communicator.Windows repo root from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void ShareSheet_CreatedUrl_TextBox_UsesOneWayBinding()
    {
        var path = Path.Combine(RepoRoot(), "ProtoLink.Communicator.Windows", "Views", "ShareSheetView.xaml");
        var text = File.ReadAllText(path);
        Assert.Contains(
            @"Text=""{Binding CreatedUrl, Mode=OneWay}""",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            @"Text=""{Binding CreatedUrl}""",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            @"Text=""{Binding CreatedUrl, Mode=TwoWay}""",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ShareSheetViewModel_CreatedUrl_SetterIsPrivate()
    {
        var path = Path.Combine(RepoRoot(), "ProtoLink.Communicator.Windows", "ViewModels", "Share", "ShareSheetViewModel.cs");
        var text = File.ReadAllText(path);
        // Property block must keep private set so UI cannot write back (OneWay required).
        var match = Regex.Match(
            text,
            @"public string\? CreatedUrl\s*\{(?<body>[\s\S]*?)\n    \}",
            RegexOptions.Multiline);
        Assert.True(match.Success, "Could not locate CreatedUrl property in ShareSheetViewModel.");
        Assert.Contains("private set", match.Groups["body"].Value, StringComparison.Ordinal);
    }
}
