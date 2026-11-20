using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>#comm-win-sick-connection — prefer online + ASAP cache + soft-fail keep policy.</summary>
[Trait("Category", "Offline")]
public class PreferOnlineUiCacheTests
{
    [Fact]
    public void PreferOnline_AlwaysRefreshesAfterCachePaint()
    {
        Assert.True(PreferOnlineUiCache.ShouldRefreshOnlineAfterCachePaint);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void PaintCacheBeforeOnline_WhenPayloadExists(bool hasCache, bool expected) =>
        Assert.Equal(expected, PreferOnlineUiCache.ShouldPaintCacheBeforeOnline(hasCache));

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void WriteCacheAsap_OnlyOnOnlineSuccess(bool success, bool expected) =>
        Assert.Equal(expected, PreferOnlineUiCache.ShouldWriteCacheAsap(success));

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void KeepCacheOnSoftFail_RequiresConnectivityAndPayload(
        bool connectivity,
        bool hasCache,
        bool expected) =>
        Assert.Equal(expected, PreferOnlineUiCache.ShouldKeepCacheOnSoftFail(connectivity, hasCache));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ClearChromeForLoad_OnlyWhenNoCache(bool hasCache, bool expectedClear) =>
        Assert.Equal(expectedClear, PreferOnlineUiCache.ShouldClearChromeForLoad(hasCache));

    [Theory]
    [InlineData(2, 2, true)]
    [InlineData(3, 2, true)]
    [InlineData(1, 2, false)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, true)]
    public void ReplaceContactsList_BlocksThinOnlineOverRichPaint(
        int online,
        int painted,
        bool expected) =>
        Assert.Equal(expected, PreferOnlineUiCache.ShouldReplaceContactsList(online, painted));

    [Theory]
    [InlineData(2, 2, true)]
    [InlineData(1, 2, false)]
    [InlineData(3, 1, true)]
    public void ReplaceContactsCache_BlocksShrink(int incoming, int existing, bool expected) =>
        Assert.Equal(expected, PreferOnlineUiCache.ShouldReplaceContactsCache(incoming, existing));
}
