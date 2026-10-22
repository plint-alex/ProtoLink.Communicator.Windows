using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Guards Publish-Release-To-Cloud.ps1 D10 wiring: snapshot+prune before latest overwrite.
/// </summary>
[Trait("Category", "Offline")]
public class PublishReleaseArchiveScriptTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var script = Path.Combine(dir.FullName, "tools", "Publish-Release-To-Cloud.ps1");
            if (File.Exists(script))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate Communicator.Windows repo root from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void PublishScript_ShortCircuitsEmptyShouldSnapshot_BeforeCli()
    {
        // PS 5.1 strips empty native argv; empty current must not call should-snapshot CLI.
        var publish = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "Publish-Release-To-Cloud.ps1"));
        var fn = publish.IndexOf("function Test-ShouldSnapshotVersion", StringComparison.Ordinal);
        Assert.True(fn > 0);
        var body = publish[fn..];
        var nextFn = body.IndexOf("\nfunction ", 1, StringComparison.Ordinal);
        if (nextFn > 0)
            body = body[..nextFn];
        Assert.Contains("IsNullOrWhiteSpace($CurrentVersion)", body, StringComparison.Ordinal);
        Assert.Contains("IsNullOrWhiteSpace($NewVersion)", body, StringComparison.Ordinal);
        var shortCircuit = body.IndexOf("IsNullOrWhiteSpace($CurrentVersion)", StringComparison.Ordinal);
        var cliCall = body.IndexOf("@(\"should-snapshot\"", StringComparison.Ordinal);
        Assert.True(shortCircuit > 0 && cliCall > shortCircuit,
            "Empty-version short-circuit must run before Invoke-ArchiveRulesCli should-snapshot.");
    }

    [Fact]
    public void PublishScript_SnapshotsBeforePublishFile()
    {
        var publish = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "Publish-Release-To-Cloud.ps1"));
        Assert.Contains("function Invoke-SnapshotPreviousLatestToHistory", publish, StringComparison.Ordinal);
        Assert.Contains("function Invoke-PrunePlatformHistory", publish, StringComparison.Ordinal);
        Assert.Contains("PublishReleaseArchive", publish, StringComparison.Ordinal);
        Assert.Contains("--copy-from", publish, StringComparison.Ordinal);

        var snapWin = publish.IndexOf("Invoke-SnapshotPreviousLatestToHistory", StringComparison.Ordinal);
        var publishWin = publish.IndexOf(
            "Publish-File -EntityId $artifactWindowsId -Path $WindowsSetupPath",
            StringComparison.Ordinal);
        Assert.True(snapWin > 0 && publishWin > snapWin,
            "Windows snapshot must run before Publish-File overwrite of stable latest id.");

        var snapAnd = publish.IndexOf(
            "PlatformLabel \"Android\"",
            StringComparison.Ordinal);
        var publishAnd = publish.IndexOf(
            "Publish-File -EntityId $artifactAndroidId -Path $AndroidApkPath",
            StringComparison.Ordinal);
        Assert.True(snapAnd > 0 && publishAnd > snapAnd,
            "Android snapshot must run before Publish-File overwrite of stable latest id.");
    }

    [Fact]
    public void CloudBlobUpload_SupportsCopyFrom()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "CloudBlobUpload", "Program.cs"));
        Assert.Contains("--copy-from", program, StringComparison.Ordinal);
        Assert.Contains("CopyObjectAsync", program, StringComparison.Ordinal);
    }
}
