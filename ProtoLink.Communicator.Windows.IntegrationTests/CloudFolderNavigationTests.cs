using System.IO;
using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Offline coverage for Cloud folder change-then-wait (#cloud-folder-responsiveness).</summary>
[Trait("Category", "Offline")]
public class CloudFolderNavigationTests
{
    [Fact]
    public void AfterChromePaint_MatchesShellTabDeferral()
    {
        Assert.Equal(ShellTabActivation.AfterChromePaint, CloudFolderNavigation.AfterChromePaint);
    }

    [Fact]
    public void BuildAncestorTrail_WalksDepthBackToRoot()
    {
        var root = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var rows = new (Guid Id, string Name, int Depth)[]
        {
            (root, "Root", 0),
            (a, "A", 1),
            (b, "B", 2)
        };

        var trail = CloudFolderNavigation.BuildAncestorTrail(rows, b);
        Assert.Equal(3, trail.Count);
        Assert.Equal(root, trail[0].Id);
        Assert.Equal(a, trail[1].Id);
        Assert.Equal(b, trail[2].Id);
    }

    [Fact]
    public void BuildAncestorTrail_UnknownId_Empty()
    {
        var rows = new (Guid Id, string Name, int Depth)[]
        {
            (Guid.NewGuid(), "Root", 0)
        };
        Assert.Empty(CloudFolderNavigation.BuildAncestorTrail(rows, Guid.NewGuid()));
    }

    [Fact]
    public void ComposeBreadcrumb_PrefixesHubAboveTreeRoot()
    {
        var hub = Guid.NewGuid();
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var existing = new (Guid Id, string Name)[]
        {
            (hub, "Cloud"),
            (root, "My Cloud"),
            (Guid.NewGuid(), "Old")
        };
        var tree = new (Guid Id, string Name)[]
        {
            (root, "My Cloud"),
            (child, "Child")
        };

        var composed = CloudFolderNavigation.ComposeBreadcrumb(existing, tree);
        Assert.Equal(3, composed.Count);
        Assert.Equal(hub, composed[0].Id);
        Assert.Equal(root, composed[1].Id);
        Assert.Equal(child, composed[2].Id);
    }

    [Fact]
    public void SumLocalBytes_CountsNestedFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pl-cloud-nav-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var nested = Path.Combine(dir, "sub");
            Directory.CreateDirectory(nested);
            File.WriteAllBytes(Path.Combine(dir, "a.bin"), new byte[10]);
            File.WriteAllBytes(Path.Combine(nested, "b.bin"), new byte[5]);
            Assert.Equal(15, CloudFolderNavigation.SumLocalBytes(dir));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
