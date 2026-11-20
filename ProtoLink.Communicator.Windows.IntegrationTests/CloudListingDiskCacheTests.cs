using ProtoLink.Communicator.Windows.Services.Cloud;
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
}
