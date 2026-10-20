using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Guards Publish-Release-To-Cloud.ps1: after addFile (or MinIO), CloudFile name must be forced
/// to the uploaded leaf. API addFile keeps a prior non-synthetic name, which broke Apply reuse.
/// </summary>
[Trait("Category", "Offline")]
public class PublishReleaseCloudFileNameScriptTests
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
    public void PublishFile_AlwaysCallsSetCloudFileUploadMetadata()
    {
        var publish = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "Publish-Release-To-Cloud.ps1"));
        Assert.Contains("function Set-CloudFileUploadMetadata", publish, StringComparison.Ordinal);
        Assert.Contains("Both paths: force leaf name", publish, StringComparison.Ordinal);
        Assert.Contains("Set-CloudFileUploadMetadata -EntityId $EntityId -FileName $fileName", publish, StringComparison.Ordinal);

        var addFileOk = publish.IndexOf("addFile ok:", StringComparison.Ordinal);
        var metaCall = publish.IndexOf("Set-CloudFileUploadMetadata -EntityId $EntityId -FileName $fileName", StringComparison.Ordinal);
        Assert.True(addFileOk > 0 && metaCall > addFileOk,
            "Set-CloudFileUploadMetadata must run after addFile success (not only on MinIO).");
    }

    [Fact]
    public void VerifyScript_AssertsCloudFileDisplayName()
    {
        var verify = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "Publish-Release-Verify.ps1"));
        Assert.Contains("function Assert-CloudFileDisplayName", verify, StringComparison.Ordinal);
        Assert.Contains("addFile preserves a prior display name", verify, StringComparison.Ordinal);

        var publish = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "Publish-Release-To-Cloud.ps1"));
        Assert.Contains("Assert-CloudFileDisplayName", publish, StringComparison.Ordinal);
    }
}
