using System.Diagnostics;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Guards Publish-Release-To-Cloud.ps1 Find-NewestArtifact: LocalStand *-stand* stubs
/// must not win default newest pick over a real release artifact.
/// </summary>
[Trait("Category", "Offline")]
public class PublishReleaseNewestArtifactScriptTests
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

    private static string PublishScriptPath() =>
        Path.Combine(RepoRoot(), "tools", "Publish-Release-To-Cloud.ps1");

    [Fact]
    public void FindNewestArtifact_ExcludesStandAndReleaseJsonInScript()
    {
        var publish = File.ReadAllText(PublishScriptPath());
        Assert.Contains("function Find-NewestArtifact", publish, StringComparison.Ordinal);
        Assert.Contains("*.release.json", publish, StringComparison.Ordinal);
        Assert.Contains("*-stand*", publish, StringComparison.Ordinal);

        var fn = publish.IndexOf("function Find-NewestArtifact", StringComparison.Ordinal);
        var where = publish.IndexOf("Where-Object", fn, StringComparison.Ordinal);
        var sort = publish.IndexOf("Sort-Object LastWriteTime", fn, StringComparison.Ordinal);
        Assert.True(fn > 0 && where > fn && sort > where);
        var whereClause = publish.Substring(where, sort - where);
        Assert.Contains("*-stand*", whereClause, StringComparison.Ordinal);
        Assert.Contains("*.release.json", whereClause, StringComparison.Ordinal);
    }

    [Fact]
    public void FindNewestArtifact_SkipsNewerStandStubWhenNonStandExists()
    {
        var temp = Path.Combine(Path.GetTempPath(), "pl-newest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var realName = "ProtoLink.Communicator.Windows-Setup-1.0.13.exe";
            var standName = "ProtoLink.Communicator.Windows-Setup-1.0.14-stand.exe";
            var real = Path.Combine(temp, realName);
            var stand = Path.Combine(temp, standName);
            var meta = Path.Combine(temp, realName + ".release.json");
            File.WriteAllText(real, "real");
            File.WriteAllText(stand, "stand");
            File.WriteAllText(meta, "{\"version\":\"1.0.13\"}");
            File.SetLastWriteTimeUtc(real, DateTime.UtcNow.AddHours(-2));
            File.SetLastWriteTimeUtc(stand, DateTime.UtcNow.AddMinutes(-1));
            File.SetLastWriteTimeUtc(meta, DateTime.UtcNow); // newest on disk; must still be ignored

            var picked = InvokeFindNewestFromPublishScript(
                temp,
                "ProtoLink.Communicator.Windows-Setup-*.exe");

            Assert.False(string.IsNullOrWhiteSpace(picked), "expected a non-stand artifact");
            Assert.Equal(realName, Path.GetFileName(picked), StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("-stand", Path.GetFileName(picked), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void FindNewestArtifact_ReturnsEmptyWhenOnlyStandStubsExist()
    {
        var temp = Path.Combine(Path.GetTempPath(), "pl-newest-onlystand-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var stand = Path.Combine(temp, "ProtoLink.Communicator.Android-1.0.4-stand.apk");
            File.WriteAllText(stand, "stand");
            File.SetLastWriteTimeUtc(stand, DateTime.UtcNow);

            var picked = InvokeFindNewestFromPublishScript(
                temp,
                "ProtoLink.Communicator.Android-*.apk");

            Assert.True(string.IsNullOrEmpty(picked), "only-stand dir must not pick a stub; got: " + picked);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Loads Find-NewestArtifact from the real publish script (AST) and invokes it —
    /// so the test fails if the script filter regresses, not only if a duplicated copy does.
    /// </summary>
    private static string InvokeFindNewestFromPublishScript(string directory, string filter)
    {
        var scriptPath = PublishScriptPath().Replace("'", "''");
        var dirLit = directory.Replace("'", "''");
        var filterLit = filter.Replace("'", "''");
        var ps = $@"
$ErrorActionPreference = 'Stop'
$errs = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile('{scriptPath}', [ref]$null, [ref]$errs)
if ($errs -and $errs.Count -gt 0) {{ throw ('parse errors: ' + ($errs | Out-String)) }}
$fn = $ast.FindAll({{ param($a) $a -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $a.Name -eq 'Find-NewestArtifact' }}, $true) | Select-Object -First 1
if (-not $fn) {{ throw 'Find-NewestArtifact not found in publish script' }}
Invoke-Expression $fn.Extent.Text
$r = Find-NewestArtifact -Directory '{dirLit}' -Filter '{filterLit}'
if ($null -eq $r) {{ $r = '' }}
Write-Output $r
";
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start powershell");
        proc.StandardInput.Write(ps);
        proc.StandardInput.Close();
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(60_000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException("Find-NewestArtifact PowerShell invoke timed out");
        }
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"Find-NewestArtifact invoke failed ({proc.ExitCode}): {stderr}\n{stdout}");

        return (stdout ?? "").Trim();
    }
}
