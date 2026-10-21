using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class CloudCreateFabVisibilityTests
{
    [Fact]
    public void IsVisible_True_WhenMutableFolderHasItems()
    {
        Assert.True(CloudCreateFabVisibility.IsVisible(canMutateListedFolder: true, isEmpty: false));
    }

    [Fact]
    public void IsVisible_False_WhenReadOnlySharedScope()
    {
        Assert.False(CloudCreateFabVisibility.IsVisible(canMutateListedFolder: false, isEmpty: false));
    }

    [Fact]
    public void IsVisible_False_WhenEmpty_KeepsCenteredUploadOnly()
    {
        Assert.False(CloudCreateFabVisibility.IsVisible(canMutateListedFolder: true, isEmpty: true));
    }
}
