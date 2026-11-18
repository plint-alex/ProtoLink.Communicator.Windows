using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Force lock-wait / download statuses must stay visible over listing Ready/Loading and the
/// network banner (tester FAIL 1.0.87 FORCE_NO_PROGRESS).
/// </summary>
[Trait("Category", "Offline")]
public class ForceProgressStatusTests
{
    [Theory]
    [InlineData("Waiting for sync lock…")]
    [InlineData("Waiting for sync lock")]
    [InlineData("Force download…")]
    [InlineData("Force download complete.")]
    [InlineData("Force upload…")]
    [InlineData("Timed out waiting for sync lock.")]
    public void IsForceProgressStatus_RecognizesForceLane(string status)
    {
        Assert.True(CloudViewModel.IsForceProgressStatus(status));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ready")]
    [InlineData("Loading...")]
    [InlineData("Upload paused.")]
    [InlineData("Uploading local changes…")]
    public void IsForceProgressStatus_RejectsListingAndUploadNoise(string? status)
    {
        Assert.False(CloudViewModel.IsForceProgressStatus(status));
    }
}
