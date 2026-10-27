using System.Windows.Threading;

namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Messenger contact change-then-wait helpers (#contacts-responsiveness / #ui-change-then-wait).
/// Selection/chrome must update before message GetEntities, attachment hydrate, or unfurl work.
/// </summary>
public static class ContactSelection
{
    /// <summary>Same priority as <see cref="ShellTabActivation.AfterChromePaint"/>.</summary>
    public static DispatcherPriority AfterChromePaint { get; } = ShellTabActivation.AfterChromePaint;

    public const string LoadingStatus = "Loading…";

    /// <summary>
    /// True when switching to a different contact (or clearing) so the chat pane must not keep
    /// the previous partner's messages while the new thread loads.
    /// </summary>
    public static bool ShouldClearMessagesForSelection(Guid? previousContactId, Guid? nextContactId)
    {
        if (nextContactId is null) return true;
        if (previousContactId is null) return false;
        return previousContactId.Value != nextContactId.Value;
    }

    /// <summary>
    /// Whether the chat pane should show loading after chrome paints: new/changed contact
    /// with no in-memory thread cache yet.
    /// </summary>
    public static bool ShouldShowLoading(Guid? nextContactId, bool hasCachedThread) =>
        nextContactId is not null && !hasCachedThread;

    public static void ScheduleAfterChromePaint(Dispatcher dispatcher, Action work) =>
        ShellTabActivation.ScheduleAfterChromePaint(dispatcher, work);

    public static void ScheduleAfterChromePaint(Dispatcher dispatcher, Func<Task> work) =>
        ShellTabActivation.ScheduleAfterChromePaint(dispatcher, work);
}
