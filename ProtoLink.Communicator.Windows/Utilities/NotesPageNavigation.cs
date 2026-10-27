using System.Windows.Threading;

namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Notes page change-then-wait helpers (#notes-responsiveness / #ui-change-then-wait).
/// Tree selection / header chrome must update before sync-meta Resolve, sibling enumerate,
/// index read, or WebView NavigateToString.
/// </summary>
public static class NotesPageNavigation
{
    /// <summary>Same priority as <see cref="ShellTabActivation.AfterChromePaint"/>.</summary>
    public static DispatcherPriority AfterChromePaint { get; } = ShellTabActivation.AfterChromePaint;

    public const string LoadingStatus = "Loading…";

    /// <summary>
    /// True when opening a different page (or force-reload) so chrome must show loading
    /// before heavy work runs.
    /// </summary>
    public static bool ShouldShowLoading(string? previousPath, string nextPath, bool forceReload)
    {
        if (string.IsNullOrWhiteSpace(nextPath)) return false;
        if (forceReload) return true;
        if (string.IsNullOrEmpty(previousPath)) return true;
        return !string.Equals(previousPath, nextPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Clear the previous page's editor document when switching notes so the pane does not keep
    /// stale HTML while the new page loads (mirrors Contacts clearing the prior thread).
    /// </summary>
    public static bool ShouldClearEditorForNavigation(string? previousPath, string nextPath, bool forceReload) =>
        ShouldShowLoading(previousPath, nextPath, forceReload);

    public static void ScheduleAfterChromePaint(Dispatcher dispatcher, Action work) =>
        ShellTabActivation.ScheduleAfterChromePaint(dispatcher, work);

    public static void ScheduleAfterChromePaint(Dispatcher dispatcher, Func<Task> work) =>
        ShellTabActivation.ScheduleAfterChromePaint(dispatcher, work);

    /// <summary>Yield until after chrome/render so selection + Loading can paint first.</summary>
    public static async Task YieldForChromePaintAsync(Dispatcher? dispatcher)
    {
        if (dispatcher != null)
            await dispatcher.InvokeAsync(static () => { }, AfterChromePaint);
        else
            await Task.Yield();
    }
}
