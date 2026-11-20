using System.Net.Http;
using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Open-chat Loading must clear within InteractiveHttpTimeout (Wave B AT-M11 / tip over-12s FAIL).
/// </summary>
[Trait("Category", "Offline")]
public class MessengerChatLoadTests
{
    [Fact]
    public void InteractiveHttpTimeout_IsUnderTwelveSeconds_WithMargin()
    {
        Assert.Equal(TimeSpan.FromSeconds(8), MessengerChatLoad.InteractiveHttpTimeout);
        Assert.True(MessengerChatLoad.InteractiveHttpTimeout <= TimeSpan.FromSeconds(10));
        Assert.True(MessengerChatLoad.InteractiveHttpTimeout >= TimeSpan.FromSeconds(5));
        // Shared Messenger HttpClient stays longer (MainWindow 45s) so Contacts init is not starved.
    }

    [Fact]
    public async Task AwaitWithinBudgetAsync_ThrowsWhenBudgetElapses_EvenIfTaskStillRunning()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        var never = Task.Delay(Timeout.InfiniteTimeSpan);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await MessengerChatLoad.AwaitWithinBudgetAsync(never, budget.Token));
    }

    [Fact]
    public void InteractiveHttpTimeout_LeavesMarginUnderTwelveSecondWaveB()
    {
        // Select-time Delay + UIA poll slack must still clear before AC ≤12s.
        Assert.True(MessengerChatLoad.InteractiveHttpTimeout + TimeSpan.FromSeconds(3)
            <= TimeSpan.FromSeconds(12));
    }

    [Fact]
    public void ShouldClearLoadingOnFault_AnyException_IncludingNonConnectivity()
    {
        Assert.True(MessengerChatLoad.ShouldClearLoadingOnFault(new InvalidOperationException("parse")));
        Assert.True(MessengerChatLoad.ShouldClearLoadingOnFault(new TaskCanceledException("timeout")));
        Assert.True(MessengerChatLoad.ShouldClearLoadingOnFault(new HttpRequestException("refused")));
        Assert.False(MessengerChatLoad.ShouldClearLoadingOnFault(null));
    }

    [Fact]
    public async Task ObserveAbandonedSoft_FaultedTask_DoesNotThrow()
    {
        var faulted = Task.FromException(new ObjectDisposedException("System.Threading.CancellationTokenSource"));
        MessengerChatLoad.ObserveAbandonedSoft(faulted);
        // Give continuation a tick; must not surface unobserved.
        await Task.Delay(50);
        Assert.True(faulted.IsFaulted);
    }

    [Fact]
    public async Task CancelObserveAndDispose_DoesNotDisposeUntilWorkCompletes()
    {
        var cts = new CancellationTokenSource();
        var tcs = new TaskCompletionSource();
        MessengerChatLoad.CancelObserveAndDispose(cts, tcs.Task);
        // CTS must still accept Cancel while work incomplete (not disposed yet).
        Assert.True(cts.IsCancellationRequested);
        tcs.SetException(new ObjectDisposedException("System.Threading.CancellationTokenSource"));
        await Task.Delay(100);
        Assert.Throws<ObjectDisposedException>(() => cts.Token.Register(() => { }));
    }
}
