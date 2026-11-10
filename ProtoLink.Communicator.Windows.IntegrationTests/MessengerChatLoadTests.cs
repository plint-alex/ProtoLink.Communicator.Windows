using System.Net.Http;
using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Open-chat Loading must clear within InteractiveHttpTimeout (Wave B AT-M11 / tip 1.0.61 hang).
/// </summary>
[Trait("Category", "Offline")]
public class MessengerChatLoadTests
{
    [Fact]
    public void InteractiveHttpTimeout_IsAboutTenSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), MessengerChatLoad.InteractiveHttpTimeout);
        Assert.True(MessengerChatLoad.InteractiveHttpTimeout <= TimeSpan.FromSeconds(10));
        Assert.True(MessengerChatLoad.InteractiveHttpTimeout >= TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ShouldClearLoadingOnFault_AnyException_IncludingNonConnectivity()
    {
        Assert.True(MessengerChatLoad.ShouldClearLoadingOnFault(new InvalidOperationException("parse")));
        Assert.True(MessengerChatLoad.ShouldClearLoadingOnFault(new TaskCanceledException("timeout")));
        Assert.True(MessengerChatLoad.ShouldClearLoadingOnFault(new HttpRequestException("refused")));
        Assert.False(MessengerChatLoad.ShouldClearLoadingOnFault(null));
    }
}
