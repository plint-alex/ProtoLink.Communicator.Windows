using System.Windows.Threading;
using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Offline coverage for shell-tab change-then-wait (#shell-tab-responsiveness).
/// Heavy activate work must schedule below Render so tab chrome can paint first.
/// Interactive vs Sync GetEntities lane split remains in <see cref="GetEntitiesThrottlingHandlerTests"/>.
/// </summary>
[Trait("Category", "Offline")]
public class ShellTabActivationTests
{
    [Fact]
    public void AfterChromePaint_RunsAfterRenderAndLoaded()
    {
        Assert.True((int)ShellTabActivation.AfterChromePaint < (int)DispatcherPriority.Render);
        Assert.True((int)ShellTabActivation.AfterChromePaint < (int)DispatcherPriority.Loaded);
        Assert.True((int)ShellTabActivation.AfterChromePaint < (int)DispatcherPriority.Input);
        Assert.Equal(DispatcherPriority.Background, ShellTabActivation.AfterChromePaint);
    }

    [Fact]
    public void ScheduleAfterChromePaint_InvokesWorkAfterHigherPriorityOps()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var ran = false;
        ShellTabActivation.ScheduleAfterChromePaint(dispatcher, () => ran = true);
        dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.True(ran);
    }
}
