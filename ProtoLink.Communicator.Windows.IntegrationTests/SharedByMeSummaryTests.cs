using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>AT-S53 Shared by me subtitle + empty strings (offline).</summary>
[Trait("Category", "Offline")]
public class SharedByMeSummaryTests
{
    [Fact]
    public void RowSubtitle_PeopleGuestsPublic_EnRu()
    {
        var en = SharedByMeSummary.RowSubtitle(
            "en",
            new[] { "Ada", "Bob" },
            peopleMoreCount: 1,
            liveGuestLinkCount: 2,
            hasPublicAccess: true);
        Assert.Contains("Ada, Bob +1 more", en, StringComparison.Ordinal);
        Assert.Contains("2 guest links", en, StringComparison.Ordinal);
        Assert.Contains("Anyone on the internet", en, StringComparison.Ordinal);

        var ru = SharedByMeSummary.RowSubtitle(
            "ru-RU",
            new[] { "Анна" },
            peopleMoreCount: 0,
            liveGuestLinkCount: 1,
            hasPublicAccess: false);
        Assert.Contains("Анна", ru, StringComparison.Ordinal);
        Assert.Contains("1 гостевая ссылка", ru, StringComparison.Ordinal);
        Assert.DoesNotContain("интернете", ru, StringComparison.Ordinal);
    }

    [Fact]
    public void RowSubtitle_GuestsOnly_Or_PublicOnly_Or_Empty()
    {
        Assert.Equal(
            "2 guest links",
            SharedByMeSummary.RowSubtitle("en", Array.Empty<string>(), 0, 2, false));
        Assert.Equal(
            string.Empty,
            SharedByMeSummary.RowSubtitle("en", Array.Empty<string>(), 0, 0, false));
        Assert.Equal(
            "Anyone on the internet",
            SharedByMeSummary.RowSubtitle("en", Array.Empty<string>(), 0, 0, true));
    }

    [Fact]
    public void SharedByMe_Strings_EnRuParity()
    {
        Assert.Equal("Shared by me", ShareSheetStrings.SharedByMe("en"));
        Assert.Equal("Я поделился", ShareSheetStrings.SharedByMe("ru-RU"));
        Assert.Equal("Nothing shared yet", ShareSheetStrings.EmptySharedByMeTitle("en"));
        Assert.Equal(
            "When you give someone access, it shows up here.",
            ShareSheetStrings.EmptySharedByMeBody("en"));
        Assert.Equal("Пока ничем не поделились", ShareSheetStrings.EmptySharedByMeTitle("ru"));
        Assert.Contains("здесь", ShareSheetStrings.EmptySharedByMeBody("ru"), StringComparison.Ordinal);
    }

    [Fact]
    public void SharedByMeId_IsVirtual_AndDistinct()
    {
        Assert.True(CloudVirtualNodes.IsVirtual(CloudVirtualNodes.SharedByMeId));
        Assert.Equal(
            Guid.Parse("a4444444-4444-4444-8444-444444444444"),
            CloudVirtualNodes.SharedByMeId);
        Assert.NotEqual(CloudVirtualNodes.SharedByMeId, CloudVirtualNodes.SharedWithMeId);
        Assert.NotEqual(CloudVirtualNodes.SharedByMeId, CloudVirtualNodes.RecentId);
        Assert.NotEqual(CloudVirtualNodes.SharedByMeId, CloudVirtualNodes.HubId);
    }
}
