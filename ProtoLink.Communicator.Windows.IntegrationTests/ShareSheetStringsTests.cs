using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Share-sheet-ux string parity (AT-S41 / UX-S10).</summary>
[Trait("Category", "Offline")]
public class ShareSheetStringsTests
{
    [Fact]
    public void GuestLinkNames_English()
    {
        Assert.Equal("Share", ShareSheetStrings.ShareButton("en-US"));
        Assert.Equal("Share…", ShareSheetStrings.ShareMenu("en-US"));
        Assert.Equal("Not in Cloud yet", ShareSheetStrings.NotInCloudYet("en"));
        Assert.Equal("Guest link created", ShareSheetStrings.GuestLinkCreated(null));
        Assert.Equal("+ Create guest link", ShareSheetStrings.CreateGuestLink("en"));
        Assert.Equal("Turn off", ShareSheetStrings.TurnOff("en-US"));
        Assert.Equal("Waiting for network…", ShareSheetStrings.OfflineBanner(null));
        Assert.Equal("Available when you're back online", ShareSheetStrings.OfflineMutationTooltip("en"));
        Assert.DoesNotContain("token", ShareSheetStrings.GuestLinksHelper("en"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", ShareSheetStrings.NoGuestLinksYet("en"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GuestLinkNames_Russian()
    {
        Assert.Equal("Поделиться", ShareSheetStrings.ShareButton("ru-RU"));
        Assert.Equal("Поделиться…", ShareSheetStrings.ShareMenu("ru-RU"));
        Assert.Equal("Ещё не в облаке", ShareSheetStrings.NotInCloudYet("ru"));
        Assert.Equal("Гостевая ссылка создана", ShareSheetStrings.GuestLinkCreated("ru"));
        Assert.Equal("+ Создать гостевую ссылку", ShareSheetStrings.CreateGuestLink("ru-RU"));
        Assert.Equal("Отключить", ShareSheetStrings.TurnOff("ru"));
        Assert.Equal("Ожидание сети…", ShareSheetStrings.OfflineBanner("ru-RU"));
        Assert.Equal("Доступно, когда появится сеть", ShareSheetStrings.OfflineMutationTooltip("ru"));
    }

    [Fact]
    public void LinkVersusGuestLink_HelpersDiffer()
    {
        var link = ShareSheetStrings.LinkHelper("en");
        var guest = ShareSheetStrings.GuestLinksHelper("en");
        Assert.Contains("already have access", link, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no account", guest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("guest", link, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FooterStatesFifteenMinuteLag()
    {
        Assert.Contains("15", ShareSheetStrings.FooterLag("en"));
        Assert.Contains("15", ShareSheetStrings.FooterLag("ru-RU"));
    }
}
