using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>E12 / AT-SFD offline: shared delete target detect + me-only path choice.</summary>
[Trait("Category", "Offline")]
public class CloudSharedDeleteTests
{
    [Fact]
    public void IsSharedDeleteTarget_SwmShareRoot_True()
    {
        Assert.True(CloudSharedDelete.IsSharedDeleteTarget(
            isSwmShareRoot: true,
            isKnownOutboundShareRoot: false,
            hasExternalGrants: false));
    }

    [Fact]
    public void IsSharedDeleteTarget_OutboundOrGrants_True()
    {
        Assert.True(CloudSharedDelete.IsSharedDeleteTarget(false, true, false));
        Assert.True(CloudSharedDelete.IsSharedDeleteTarget(false, false, true));
    }

    [Fact]
    public void IsSharedDeleteTarget_Unshared_False()
    {
        Assert.False(CloudSharedDelete.IsSharedDeleteTarget(false, false, false));
    }

    [Fact]
    public void HasExternalGrants_SelfOnly_False()
    {
        var me = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        Assert.False(CloudSharedDelete.HasExternalGrants(new[] { me }, me));
        Assert.False(CloudSharedDelete.HasExternalGrants(Array.Empty<Guid>(), me));
        Assert.False(CloudSharedDelete.HasExternalGrants(null, me));
        Assert.False(CloudSharedDelete.HasExternalGrants(new[] { me }, Guid.Empty));
    }

    [Fact]
    public void HasExternalGrants_SelfPlusTina_True()
    {
        var me = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var tina = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        Assert.True(CloudSharedDelete.HasExternalGrants(new[] { me, tina }, me));
        Assert.True(CloudSharedDelete.HasExternalGrants(new[] { tina }, me));
    }

    [Fact]
    public void PreferLeaveForMeOnly_OnlySwm()
    {
        Assert.True(CloudSharedDelete.PreferLeaveForMeOnly(true));
        Assert.False(CloudSharedDelete.PreferLeaveForMeOnly(false));
    }

    [Fact]
    public void SharedDeleteStrings_EnRuParity()
    {
        Assert.Equal("For me only", CloudSharedDeleteStrings.ForMeOnly("en"));
        Assert.Equal("Только для меня", CloudSharedDeleteStrings.ForMeOnly("ru"));
        Assert.Equal("For all users", CloudSharedDeleteStrings.ForAll("en"));
        Assert.Equal("Для всех", CloudSharedDeleteStrings.ForAll("ru"));
        Assert.Contains("shared", CloudSharedDeleteStrings.Body("en", "Docs", true), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Docs", CloudSharedDeleteStrings.Body("ru", "Docs", true), StringComparison.Ordinal);
    }

    [Fact]
    public void CanEditHelper_MentionsAddEditDelete()
    {
        var en = ShareSheetStrings.CanEditHelper("en");
        var ru = ShareSheetStrings.CanEditHelper("ru");
        Assert.Contains("add", en, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("edit", en, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delete", en, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("добавлять", ru, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("удалять", ru, StringComparison.OrdinalIgnoreCase);
    }
}
