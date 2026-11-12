namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Open-chat history load contract (#contacts-responsiveness / Wave B AT-M11).
/// Pane must leave Loading within InteractiveHttpTimeout — never hang on blackholed HTTPS
/// or unexpected parse faults.
/// </summary>
public static class MessengerChatLoad
{
    /// <summary>
    /// Hard leave-Loading budget for open-chat (Wave B ≤~12s wall clock; margin for UIA).
    /// HttpClient CancellationToken alone can leave the await running until the response
    /// arrives (~11.5s+ on tip) — always pair with <see cref="AwaitWithinBudgetAsync"/>.
    /// Messenger HttpClient stays 45s so contacts / EnsureBag are not starved.
    /// </summary>
    public static readonly TimeSpan InteractiveHttpTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Completes when <paramref name="task"/> does, or throws when <paramref name="budget"/>
    /// cancels — even if the underlying HTTP request has not aborted yet.
    /// </summary>
    public static Task<T> AwaitWithinBudgetAsync<T>(Task<T> task, CancellationToken budget) =>
        task.WaitAsync(budget);

    /// <summary>
    /// Completes when <paramref name="task"/> does, or throws when <paramref name="budget"/>
    /// cancels — even if the underlying HTTP request has not aborted yet.
    /// </summary>
    public static Task AwaitWithinBudgetAsync(Task task, CancellationToken budget) =>
        task.WaitAsync(budget);

    /// <summary>
    /// Fire-and-forget LoadMessages must clear loading for any fault (connectivity or parse),
    /// not only <see cref="Services.ApiExceptionHelper.IsExpectedConnectivityFailure"/>.
    /// </summary>
    public static bool ShouldClearLoadingOnFault(Exception? ex) => ex != null;
}
