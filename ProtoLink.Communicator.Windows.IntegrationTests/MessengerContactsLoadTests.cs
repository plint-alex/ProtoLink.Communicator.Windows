using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Offline coverage for Messenger contacts startup (#messenger-startup-priority).</summary>
[Trait("Category", "Offline")]
public class MessengerContactsLoadTests
{
    [Fact]
    public void InteractiveHttpTimeout_IsBoundedForStartup()
    {
        Assert.Equal(TimeSpan.FromSeconds(12), MessengerContactsLoad.InteractiveHttpTimeout);
        Assert.True(MessengerContactsLoad.InteractiveHttpTimeout <= TimeSpan.FromSeconds(15));
        Assert.True(MessengerContactsLoad.InteractiveHttpTimeout >= TimeSpan.FromSeconds(8));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldShowLoading_TracksFlag(bool loading, bool expected) =>
        Assert.Equal(expected, MessengerContactsLoad.ShouldShowLoading(loading));

    [Theory]
    [InlineData(true, 0, false)]
    [InlineData(true, 3, false)]
    [InlineData(false, 0, true)]
    [InlineData(false, 2, false)]
    public void ShouldShowEmpty_OnlyAfterFinishedWithZero(bool loading, int count, bool expected) =>
        Assert.Equal(expected, MessengerContactsLoad.ShouldShowEmpty(loading, count));

    [Fact]
    public void TryParseContactUserId_FromUserIdValue()
    {
        var uid = Guid.Parse("11111111-2222-4333-8444-555555555555");
        var values = new List<EntityValue>
        {
            new() { Type = "StringValue", Value = $"userid:{uid}" }
        };
        Assert.Equal(uid, MessengerContactsLoad.TryParseContactUserId(values, null));
    }

    [Fact]
    public void TryParseContactUserId_FromCodeFallback()
    {
        var uid = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
        Assert.Equal(uid, MessengerContactsLoad.TryParseContactUserId(null, uid.ToString("D")));
    }

    [Fact]
    public void TryParseContactUserId_RejectsEmpty()
    {
        Assert.Null(MessengerContactsLoad.TryParseContactUserId(null, null));
        Assert.Null(MessengerContactsLoad.TryParseContactUserId(
            new List<EntityValue> { new() { Type = "StringValue", Value = "not-a-userid" } },
            "not-guid"));
    }
}
