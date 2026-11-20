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
        Assert.True(PreferOnlineUiCache.ShouldForceLoginRefreshAfterCachePaint);
    }

    [Fact]
    public void PreferOnline_BudgetElapsed_DoesNotStickyOfflineBanner()
    {
        // Interactive budget abandon ≠ connectivity; sticky banner left healthy Api on Waiting for network.
        Assert.False(PreferOnlineUiCache.ShouldShowOfflineBannerOnInteractiveBudgetElapsed);
    }

    [Fact]
    public void PreferOnline_CachedUserContactsBag_OnlyWhenPartnerBearing()
    {
        Assert.True(PreferOnlineUiCache.ShouldPreferCachedUserContactsBag);
        Assert.False(PreferOnlineUiCache.ShouldPersistUserContactsBagId(0));
        Assert.False(PreferOnlineUiCache.ShouldPersistUserContactsBagId(1));
        Assert.True(PreferOnlineUiCache.ShouldPersistUserContactsBagId(2));
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
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void OpenLastOpenFromListingDisk_WhenPayloadExists(bool hasListing, bool expected) =>
        Assert.Equal(expected, PreferOnlineUiCache.ShouldOpenLastOpenFromListingDisk(hasListing));

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void FallbackToHubAfterListingSoftFail_SkipsWhenPainted(
        bool isKnownRoot,
        bool hasPaintedRows,
        bool expected) =>
        Assert.Equal(
            expected,
            PreferOnlineUiCache.ShouldFallbackToHubAfterListingSoftFail(isKnownRoot, hasPaintedRows));

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

    /// <summary>
    /// TINA_FLASH_THEN_DROP: bag swap after EnsureBagIdAsync must not Clear a richer paint
    /// (startup painted self+Tina=2; thinner bag returns self-only=1).
    /// </summary>
    [Fact]
    public void BagSwap_MustNotShrinkRichPaint_SelfOnlyOverSelfPlusTina()
    {
        const int richPaintSelfPlusTina = 2;
        const int thinBagSelfOnly = 1;
        Assert.False(
            PreferOnlineUiCache.ShouldReplaceContactsList(thinBagSelfOnly, richPaintSelfPlusTina));
        Assert.True(
            PreferOnlineUiCache.ShouldReplaceContactsList(richPaintSelfPlusTina, thinBagSelfOnly));
    }

    [Theory]
    [InlineData(2, 2, true)]
    [InlineData(1, 2, false)]
    [InlineData(3, 1, true)]
    public void ReplaceContactsCache_BlocksShrink(int incoming, int existing, bool expected) =>
        Assert.Equal(expected, PreferOnlineUiCache.ShouldReplaceContactsCache(incoming, existing));
}
