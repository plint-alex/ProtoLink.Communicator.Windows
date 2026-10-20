using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class MessengerConversationScopeTests
{
    [Fact]
    public void ConversationMessagePageSize_IsModest()
    {
        Assert.Equal(100, MessengerViewModel.ConversationMessagePageSize);
        Assert.True(MessengerViewModel.ConversationMessagePageSize <= 100);
    }

    [Fact]
    public void BuildConversationUserIds_Pair_ReturnsBoth()
    {
        var me = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var partner = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var ids = MessengerViewModel.BuildConversationUserIds(me, partner);
        Assert.Equal(2, ids.Length);
        Assert.Contains(me, ids);
        Assert.Contains(partner, ids);
    }

    [Fact]
    public void BuildConversationUserIds_SelfChat_ReturnsOne()
    {
        var me = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var ids = MessengerViewModel.BuildConversationUserIds(me, me);
        Assert.Single(ids);
        Assert.Equal(me, ids[0]);
    }
}
