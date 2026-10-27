using System.Windows.Threading;
using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Offline coverage for Messenger contact change-then-wait (#contacts-responsiveness).</summary>
[Trait("Category", "Offline")]
public class ContactSelectionTests
{
    [Fact]
    public void AfterChromePaint_MatchesShellTabDeferral()
    {
        Assert.Equal(ShellTabActivation.AfterChromePaint, ContactSelection.AfterChromePaint);
        Assert.Equal(DispatcherPriority.Background, ContactSelection.AfterChromePaint);
    }

    [Fact]
    public void ShouldClearMessagesForSelection_ClearsOnPartnerChangeOrBack()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.True(ContactSelection.ShouldClearMessagesForSelection(a, b));
        Assert.True(ContactSelection.ShouldClearMessagesForSelection(a, null));
        Assert.False(ContactSelection.ShouldClearMessagesForSelection(a, a));
        Assert.False(ContactSelection.ShouldClearMessagesForSelection(null, a));
    }

    [Fact]
    public void ShouldShowLoading_WhenUncachedContact()
    {
        var id = Guid.NewGuid();
        Assert.True(ContactSelection.ShouldShowLoading(id, hasCachedThread: false));
        Assert.False(ContactSelection.ShouldShowLoading(id, hasCachedThread: true));
        Assert.False(ContactSelection.ShouldShowLoading(null, hasCachedThread: false));
    }

    [Fact]
    public void ScheduleAfterChromePaint_InvokesWorkAfterHigherPriorityOps()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var ran = false;
        ContactSelection.ScheduleAfterChromePaint(dispatcher, () => ran = true);
        dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.True(ran);
    }
}
