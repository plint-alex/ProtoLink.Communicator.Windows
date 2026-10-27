using System.Windows.Threading;

namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Shell tabs must update selection/chrome before heavy activate work
/// (<c>#ui-change-then-wait</c> / <c>#shell-tab-responsiveness</c>).
/// WPF <see cref="System.Windows.Controls.TabControl"/> remounts selected content on switch;
/// synchronous tree populate / navigation / sync-meta on Loaded freezes chrome paint.
/// </summary>
public static class ShellTabActivation
{
    /// <summary>
    /// Runs after Input/Loaded/Render so the selected tab chrome can paint first.
    /// </summary>
    public static DispatcherPriority AfterChromePaint { get; } = DispatcherPriority.Background;

    public static void ScheduleAfterChromePaint(Dispatcher dispatcher, Action work)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(work);
        dispatcher.BeginInvoke(work, AfterChromePaint);
    }

    public static void ScheduleAfterChromePaint(Dispatcher dispatcher, Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(work);
        dispatcher.BeginInvoke(new Action(() => _ = work()), AfterChromePaint);
    }
}
