namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Open-chat history load contract (#contacts-responsiveness / Wave B AT-M11).
/// Pane must leave Loading within InteractiveHttpTimeout — never hang on blackholed HTTPS
/// or unexpected parse faults.
/// </summary>
public static class MessengerChatLoad
{
    /// <summary>
    /// Hard leave-Loading budget measured from contact select (Wave B ≤~12s wall clock).
    /// Pair with <see cref="AwaitWithinBudgetAsync"/> on GetEntities <em>and</em> a select-time
    /// <c>Task.Delay</c> clear — CTS alone can stall until response, and Background deferral
    /// can delay LoadMessages start so wall clock exceeds AC.
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
