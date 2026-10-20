using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class MessengerCloudPickerGalleryUnfurlTests
{
    private static readonly Guid IdA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid IdB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid IdC = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid IdD = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public void CloudShareability_WriteOrRecipientRead()
    {
        Assert.True(MessengerCloudShareability.CanSelect(senderHasWrite: true, recipientAlreadyHasRead: false));
        Assert.True(MessengerCloudShareability.CanSelect(senderHasWrite: false, recipientAlreadyHasRead: true));
        Assert.False(MessengerCloudShareability.CanSelect(senderHasWrite: false, recipientAlreadyHasRead: false));
        Assert.Equal("You can't share this item", MessengerCloudShareability.DisabledShareSubtitleEn);
    }

    [Fact]
    public void GetEntitiesPageSize_MatchesCloudTab()
    {
        Assert.Equal(200, CloudApiService.GetEntitiesPageSize);
    }

    [Fact]
    public void Gallery_CollectsBothDirections_NewestFirst()
    {
        // AT-M32: two-each files → four tiles, newest first, both sides.
        var t1 = new DateTime(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 10, 17, 11, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        var entries = MessengerMediaGallery.Collect(new[]
        {
            (AttachmentIds: (IReadOnlyList<Guid>)new[] { IdA, IdB }, Timestamp: t1, IsFromMe: true, IsDateSeparator: false),
            (AttachmentIds: (IReadOnlyList<Guid>)new[] { IdC, IdD }, Timestamp: t3, IsFromMe: false, IsDateSeparator: false),
            (AttachmentIds: (IReadOnlyList<Guid>)Array.Empty<Guid>(), Timestamp: t2, IsFromMe: true, IsDateSeparator: false),
        });

        Assert.Equal(4, entries.Count);
        Assert.Equal(new[] { IdC, IdD, IdA, IdB }, entries.Select(e => e.EntityId).ToArray());
    }

    [Fact]
    public void Gallery_DedupesKeepingNewest()
    {
        var older = new DateTime(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        var entries = MessengerMediaGallery.Collect(new[]
        {
            (AttachmentIds: (IReadOnlyList<Guid>)new[] { IdA }, Timestamp: older, IsFromMe: true, IsDateSeparator: false),
            (AttachmentIds: (IReadOnlyList<Guid>)new[] { IdA }, Timestamp: newer, IsFromMe: false, IsDateSeparator: false),
        });
        Assert.Single(entries);
        Assert.Equal(IdA, entries[0].EntityId);
        Assert.Equal(newer, entries[0].MessageTimestamp);
    }

    [Fact]
    public void Unfurl_FindsPublicSiteGuid_IgnoresExternal()
    {
        var baseUrl = "https://protolink.ru/";
        var text = $"see https://protolink.ru/{IdA:D}/contract?lang=en and https://example.com/{IdB:D}";
        var ids = EntityLinkUnfurl.FindEntityIds(text, baseUrl);
        Assert.Equal(new[] { IdA }, ids.ToArray());

        Assert.True(EntityLinkUnfurl.TryParseEntityUrl($"https://protolink.ru/{IdA:D}/slug", baseUrl, out var parsed));
        Assert.Equal(IdA, parsed);
        Assert.False(EntityLinkUnfurl.TryParseEntityUrl($"https://evil.example/{IdA:D}", baseUrl, out _));
    }

    [Fact]
    public void Unfurl_DefaultBase_WhenNullSettings()
    {
        var ids = EntityLinkUnfurl.FindEntityIds(
            $"https://protolink.ru/{IdA:D}",
            publicSiteBaseUrl: null);
        Assert.Equal(new[] { IdA }, ids.ToArray());
    }
}
