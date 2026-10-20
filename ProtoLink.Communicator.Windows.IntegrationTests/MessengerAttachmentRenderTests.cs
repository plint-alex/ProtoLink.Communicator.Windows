using ProtoLink.Communicator.Windows.Services.Messenger;
using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class MessengerAttachmentRenderTests
{
    [Theory]
    [InlineData("image/png", true)]
    [InlineData("image/jpeg", true)]
    [InlineData("application/zip", false)]
    [InlineData("application/octet-stream", false)]
    [InlineData(null, false)]
    public void LooksLikeImageMime_MatchesContract(string? mime, bool expected) =>
        Assert.Equal(expected, MessengerAttachmentRender.LooksLikeImageMime(mime));

    [Fact]
    public void InitialKind_ImageMime_IsLoading_UntilProbe()
    {
        Assert.Equal(
            MessengerAttachmentVisualKind.Loading,
            MessengerAttachmentRender.InitialKind("image/png", forceFileRow: false));
        Assert.Equal(
            MessengerAttachmentVisualKind.File,
            MessengerAttachmentRender.InitialKind("image/png", forceFileRow: true));
        Assert.Equal(
            MessengerAttachmentVisualKind.File,
            MessengerAttachmentRender.InitialKind("application/zip", forceFileRow: false));
    }

    [Fact]
    public void AfterImageProbe_MislabelledPng_DegradesToFile()
    {
        // AT-M20: zip renamed to .png — getImage may 200 with non-image or fail decode.
        var kind = MessengerAttachmentRender.AfterImageProbe(
            forceFileRow: false,
            httpStatus: 200,
            contentType: "application/zip",
            decodedAsImage: false);
        Assert.Equal(MessengerAttachmentVisualKind.File, kind);
    }

    [Fact]
    public void AfterImageProbe_SuccessImage_IsImage()
    {
        var kind = MessengerAttachmentRender.AfterImageProbe(
            forceFileRow: false,
            httpStatus: 200,
            contentType: "image/png",
            decodedAsImage: true);
        Assert.Equal(MessengerAttachmentVisualKind.Image, kind);
    }

    [Fact]
    public void AfterImageProbe_404_IsTombstoneRemoved()
    {
        var kind = MessengerAttachmentRender.AfterImageProbe(
            forceFileRow: false,
            httpStatus: 404,
            contentType: null,
            decodedAsImage: false);
        Assert.Equal(MessengerAttachmentVisualKind.TombstoneRemoved, kind);
        Assert.Equal("File removed", MessengerAttachmentRender.TombstoneRemovedEn);
    }

    [Fact]
    public void AfterImageProbe_403_IsTombstoneNoAccess()
    {
        var kind = MessengerAttachmentRender.AfterImageProbe(
            forceFileRow: false,
            httpStatus: 403,
            contentType: null,
            decodedAsImage: false);
        Assert.Equal(MessengerAttachmentVisualKind.TombstoneNoAccess, kind);
        Assert.Equal("You don't have access to this item", MessengerAttachmentRender.TombstoneNoAccessEn);
    }

    [Fact]
    public void AfterImageProbe_SendAsFile_ForcesFileEvenOnImageSuccess()
    {
        var kind = MessengerAttachmentRender.AfterImageProbe(
            forceFileRow: true,
            httpStatus: 200,
            contentType: "image/png",
            decodedAsImage: true);
        Assert.Equal(MessengerAttachmentVisualKind.File, kind);
    }

    [Fact]
    public void SplitBatches_Twelve_YieldsTenPlusTwo()
    {
        var items = Enumerable.Range(1, 12).ToList();
        var batches = MessengerAttachmentRender.SplitBatches(items, MessengerMediaLimits.MaxAttachmentsPerMessage);
        Assert.Equal(2, batches.Count);
        Assert.Equal(10, batches[0].Count);
        Assert.Equal(2, batches[1].Count);
        Assert.Equal(Enumerable.Range(1, 10), batches[0]);
        Assert.Equal(Enumerable.Range(11, 2), batches[1]);
    }

    [Fact]
    public void SplitBatches_ExactlyTen_OneBatch()
    {
        var items = Enumerable.Range(1, 10).ToList();
        var batches = MessengerAttachmentRender.SplitBatches(items, 10);
        Assert.Single(batches);
        Assert.Equal(10, batches[0].Count);
    }

    [Fact]
    public void ScreenshotFileName_UsesLocalStampAndPng()
    {
        var name = MessengerAttachmentRender.ScreenshotFileName(new DateTime(2026, 10, 17, 18, 42, 0));
        Assert.Equal("screenshot-2026-10-17-1842.png", name);
    }

    [Fact]
    public void ThumbnailCache_EvictsByCountAndClears()
    {
        var cache = new MessengerThumbnailCache();
        for (var i = 0; i < MessengerThumbnailCache.MaxEntries + 5; i++)
        {
            cache.Set(Guid.NewGuid(), 480, 480, new byte[] { (byte)(i % 256) });
        }

        Assert.True(cache.Count <= MessengerThumbnailCache.MaxEntries);
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.TotalBytes);
    }

    [Fact]
    public void FormatByteSize_Readable()
    {
        Assert.Equal("512 B", MessengerAttachmentRender.FormatByteSize(512));
        Assert.Equal("1.5 KB", MessengerAttachmentRender.FormatByteSize(1536));
        Assert.Contains("MB", MessengerAttachmentRender.FormatByteSize(12 * 1024 * 1024));
    }
}
