using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class MapLocalFolderCtaTests
{
    [Fact]
    public void ShouldShow_OnHubWhenAuthenticated()
    {
        Assert.True(MapLocalFolderCta.ShouldShow(
            isAuthenticated: true,
            showSharedBrowseOnlyHint: false,
            isCloudHubListing: true,
            canMutateListedFolder: false,
            isInsideMappedTree: false));
    }

    [Fact]
    public void ShouldShow_InsideMappedTree_SoUserIsNotStuck()
    {
        Assert.True(MapLocalFolderCta.ShouldShow(
            isAuthenticated: true,
            showSharedBrowseOnlyHint: false,
            isCloudHubListing: false,
            canMutateListedFolder: false,
            isInsideMappedTree: true));
    }

    [Fact]
    public void ShouldHide_SharedBrowseOnly()
    {
        Assert.False(MapLocalFolderCta.ShouldShow(
            isAuthenticated: true,
            showSharedBrowseOnlyHint: true,
            isCloudHubListing: true,
            canMutateListedFolder: false,
            isInsideMappedTree: false));
    }

    [Fact]
    public void ShouldHide_WhenLoggedOut()
    {
        Assert.False(MapLocalFolderCta.ShouldShow(
            isAuthenticated: false,
            showSharedBrowseOnlyHint: false,
            isCloudHubListing: true,
            canMutateListedFolder: true,
            isInsideMappedTree: false));
    }
}
