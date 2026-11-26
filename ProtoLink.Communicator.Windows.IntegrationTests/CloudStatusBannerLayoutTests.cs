using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Cloud list-pane status chrome must reserve height on Ready/idle so the listing does not jump
/// (P1-20261126-win-cloud-status-banner-no-layout-jump). Force still beats network banner.
/// </summary>
[Trait("Category", "Offline")]
public class CloudStatusBannerLayoutTests
{
    [Theory]
    [InlineData("Ready")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Loading...")]
    [InlineData("Loading…")]
    [InlineData("Syncing…")]
    [InlineData("Uploading…")]
    [InlineData("Soft Ready")]
    [InlineData(null)]
    public void ShowStatusBanner_StaysVisibleAcrossIdleBusy_WhenNetworkOff(string? status)
    {
        Assert.True(CloudViewModel.ComputeShowStatusBanner(status, networkBannerFlag: false));
    }

    [Fact]
    public void ShowStatusBanner_NoFlipOnReadyLoadingReadyCycle()
    {
        Assert.True(CloudViewModel.ComputeShowStatusBanner("Ready", false));
        Assert.True(CloudViewModel.ComputeShowStatusBanner("Loading...", false));
        Assert.True(CloudViewModel.ComputeShowStatusBanner("Syncing…", false));
        Assert.True(CloudViewModel.ComputeShowStatusBanner("Uploading…", false));
        Assert.True(CloudViewModel.ComputeShowStatusBanner("Soft Ready", false));
        Assert.True(CloudViewModel.ComputeShowStatusBanner("Ready", false));
    }

    [Theory]
    [InlineData("Ready")]
    [InlineData("Loading...")]
    [InlineData("Syncing…")]
    [InlineData("Soft Ready")]
    public void ShowStatusBanner_YieldsToNetworkBanner_WhenNotForce(string status)
    {
        Assert.False(CloudViewModel.ComputeShowStatusBanner(status, networkBannerFlag: true));
        Assert.True(CloudViewModel.ComputeShowNetworkBanner(true, status));
    }

    [Theory]
    [InlineData("Waiting for sync lock…")]
    [InlineData("Force download…")]
    [InlineData("Force upload…")]
    [InlineData("Timed out waiting for sync lock.")]
    public void ShowStatusBanner_ForceProgressBeatsNetworkBanner(string status)
    {
        Assert.True(CloudViewModel.ComputeShowStatusBanner(status, networkBannerFlag: true));
        Assert.False(CloudViewModel.ComputeShowNetworkBanner(true, status));
    }
}
