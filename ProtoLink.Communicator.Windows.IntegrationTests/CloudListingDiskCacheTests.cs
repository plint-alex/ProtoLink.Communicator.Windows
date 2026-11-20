using ProtoLink.Communicator.Windows.Services.Cloud;
using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class CloudListingDiskCacheTests : IDisposable
{
    private readonly string _dir;
    private readonly CloudListingDiskCache _cache;

    public CloudListingDiskCacheTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(),
            "ProtoLinkTests",
            "disposable",
            "cloud-listing-disk-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _cache = new CloudListingDiskCache(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Best-effort NFR-TEST-1 cleanup.
        }
    }

    [Fact]
    public void Listing_SaveAsap_ThenLoad_RoundTrips()
    {
        var folder = Guid.NewGuid();
        var child = Guid.NewGuid();
        _cache.SaveAsap(folder, new[]
        {
            new CloudListingDiskCache.CachedListingItem(
                child,
                "Notes",
                IsFolder: true,
                MimeType: null,
                UpdateTimeUtc: new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc))
        });

        var loaded = _cache.TryLoad(folder);
        Assert.Single(loaded);
        Assert.Equal(child, loaded[0].Id);
        Assert.Equal("Notes", loaded[0].Name);
        Assert.True(loaded[0].IsFolder);
        Assert.Empty(_cache.TryLoad(Guid.NewGuid()));
    }

    [Fact]
    public void SickPaint_Policy_OpensLastOpenWhenListingDiskPresent()
    {
        var myCloud = Guid.Parse("8aec679c-a389-46b9-8105-8a5b238ee979");
        var child = Guid.Parse("ad27dde0-f6e0-42b8-be6a-95aa5716ed58");
        _cache.SaveAsap(myCloud, new[]
        {
            new CloudListingDiskCache.CachedListingItem(
                child,
                "folder-ad27dde0",
                IsFolder: true,
                MimeType: null,
                UpdateTimeUtc: DateTime.UtcNow)
        });

        var loaded = _cache.TryLoad(myCloud);
        Assert.True(PreferOnlineUiCache.ShouldOpenLastOpenFromListingDisk(loaded.Count > 0));
        Assert.False(
            PreferOnlineUiCache.ShouldFallbackToHubAfterListingSoftFail(
                isKnownCloudRoot: true,
                hasPaintedListingRows: loaded.Count > 0));
        Assert.Equal(child, loaded[0].Id);
    }
}
