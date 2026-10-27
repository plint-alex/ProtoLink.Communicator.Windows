using System.Windows.Threading;
using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Offline coverage for Notes page change-then-wait (#notes-responsiveness).</summary>
[Trait("Category", "Offline")]
public class NotesPageNavigationTests
{
    [Fact]
    public void AfterChromePaint_MatchesShellTabDeferral()
    {
        Assert.Equal(ShellTabActivation.AfterChromePaint, NotesPageNavigation.AfterChromePaint);
        Assert.Equal(DispatcherPriority.Background, NotesPageNavigation.AfterChromePaint);
    }

    [Fact]
    public void ShouldShowLoading_WhenPathChangesOrForceReload()
    {
        Assert.True(NotesPageNavigation.ShouldShowLoading(null, @"C:\Notes\A", forceReload: false));
        Assert.True(NotesPageNavigation.ShouldShowLoading(@"C:\Notes\A", @"C:\Notes\B", forceReload: false));
        Assert.True(NotesPageNavigation.ShouldShowLoading(@"C:\Notes\A", @"C:\Notes\A", forceReload: true));
        Assert.False(NotesPageNavigation.ShouldShowLoading(@"C:\Notes\A", @"C:\Notes\A", forceReload: false));
        Assert.False(NotesPageNavigation.ShouldShowLoading(@"C:\Notes\A", "", forceReload: false));
    }

    [Fact]
    public void ShouldClearEditorForNavigation_MatchesLoadingGate()
    {
        Assert.True(NotesPageNavigation.ShouldClearEditorForNavigation(@"C:\a", @"C:\b", forceReload: false));
        Assert.False(NotesPageNavigation.ShouldClearEditorForNavigation(@"C:\a", @"C:\a", forceReload: false));
    }

    [Fact]
    public void ScheduleAfterChromePaint_InvokesWorkAfterHigherPriorityOps()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var ran = false;
        NotesPageNavigation.ScheduleAfterChromePaint(dispatcher, () => ran = true);
        dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.True(ran);
    }
}
