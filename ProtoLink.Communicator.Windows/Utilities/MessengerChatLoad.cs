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
    /// Soft-observe a task abandoned by <see cref="AwaitWithinBudgetAsync"/> so its fault
    /// cannot surface as Unobserved → "Background task error" (StartupFast escalate race).
    /// </summary>
    public static void ObserveAbandonedSoft(Task? task)
    {
        if (task == null || task.IsCompleted)
        {
            // Still touch Exception to observe faulted completed tasks.
            _ = task?.Exception;
            return;
        }

        _ = ObserveAsync(task);

        static async Task ObserveAsync(Task t)
        {
            try
            {
                await t.ConfigureAwait(false);
            }
            catch
            {
                // Soft: budget already left Loading; modal must not appear.
            }
        }
    }

    /// <summary>
    /// Cancel work, soft-observe it, then dispose the CTS — never dispose while HTTP still
    /// registers on the token (ObjectDisposedException → Background task error).
    /// </summary>
    public static void CancelObserveAndDispose(CancellationTokenSource? cts, Task? abandoned)
    {
        if (cts == null)
        {
            ObserveAbandonedSoft(abandoned);
            return;
        }

        try
        {
            if (!cts.IsCancellationRequested)
                cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }

        if (abandoned == null || abandoned.IsCompleted)
        {
            _ = abandoned?.Exception;
            try
            {
                cts.Dispose();
            }
            catch
            {
                // ignore
            }

            return;
        }

        _ = CancelObserveDisposeAsync(cts, abandoned);

        static async Task CancelObserveDisposeAsync(CancellationTokenSource source, Task work)
        {
            try
            {
                await work.ConfigureAwait(false);
            }
            catch
            {
                // soft
            }
            finally
            {
                try
                {
                    source.Dispose();
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    /// <summary>
    /// Fire-and-forget LoadMessages must clear loading for any fault (connectivity or parse),
    /// not only <see cref="Services.ApiExceptionHelper.IsExpectedConnectivityFailure"/>.
    /// </summary>
    public static bool ShouldClearLoadingOnFault(Exception? ex) => ex != null;
}
