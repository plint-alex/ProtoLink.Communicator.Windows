using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Cloud Copy id — Guid D format + labels (offline; no clipboard). Virtual rows gated via LinkTargetId.</summary>
[Trait("Category", "Offline")]
public class CloudCopyIdTests
{
    [Fact]
    public void FormatEntityIdForClipboard_GuidD_NoFakeEmpty()
    {
        var id = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        Assert.Equal("a1b2c3d4-e5f6-7890-abcd-ef1234567890", ShareAccountStrings.FormatEntityIdForClipboard(id));
        Assert.Null(ShareAccountStrings.FormatEntityIdForClipboard(Guid.Empty));
    }

    [Fact]
    public void VirtualHub_IsNotARealEntityIdForCopy()
    {
        Assert.True(CloudVirtualNodes.IsVirtual(CloudVirtualNodes.HubId));
        Assert.True(CloudVirtualNodes.IsVirtual(CloudVirtualNodes.SharedWithMeId));
        Assert.True(CloudVirtualNodes.IsVirtual(CloudVirtualNodes.RecentId));
    }

    [Fact]
    public void CopyIdLabel_MatchesNotesWording()
    {
        Assert.Equal("Copy id", ShareAccountStrings.CopyIdLabel("en"));
        Assert.Equal("Копировать id", ShareAccountStrings.CopyIdLabel("ru"));
    }
}
