using System.Diagnostics;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Offline CLI smoke for tools/PublishReleaseArchive (PS 5.1 empty-argv contract).
/// </summary>
[Trait("Category", "Offline")]
public class PublishReleaseArchiveCliTests
{
    private static string ProjectPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var csproj = Path.Combine(dir.FullName, "tools", "PublishReleaseArchive", "PublishReleaseArchive.csproj");
            if (File.Exists(csproj))
                return csproj;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate PublishReleaseArchive.csproj from " + AppContext.BaseDirectory);
    }

    private static (int Exit, string StdOut, string StdErr) RunCli(params string[] afterDashDash)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(ProjectPath());
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("Release");
        psi.ArgumentList.Add("--no-launch-profile");
        psi.ArgumentList.Add("--");
        foreach (var a in afterDashDash)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(120_000);
        return (p.ExitCode, stdout.Trim(), stderr.Trim());
    }

    [Fact]
    public void ShouldSnapshot_EmptyCurrent_PrintsFalse_Exit0()
    {
        var (exit, stdout, _) = RunCli("should-snapshot", "", "1.0.14");
        Assert.Equal(0, exit);
        Assert.Equal("false", stdout);
    }

    [Fact]
    public void ShouldSnapshot_StrippedEmptyCurrent_OneArg_PrintsFalse_Exit0()
    {
        // Simulates Windows PowerShell 5.1 dropping the empty current argv.
        var (exit, stdout, stderr) = RunCli("should-snapshot", "1.0.14");
        Assert.Equal(0, exit);
        Assert.Equal("false", stdout);
        Assert.DoesNotContain("Usage", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldSnapshot_SemverChange_PrintsTrue()
    {
        var (exit, stdout, _) = RunCli("should-snapshot", "1.0.13", "1.0.14");
        Assert.Equal(0, exit);
        Assert.Equal("true", stdout);
    }

    [Fact]
    public void ShouldSnapshot_SameVersion_PrintsFalse()
    {
        var (exit, stdout, _) = RunCli("should-snapshot", "1.0.14", "1.0.14");
        Assert.Equal(0, exit);
        Assert.Equal("false", stdout);
    }
}
