namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Open-chat history load contract (#contacts-responsiveness / Wave B AT-M11).
/// Pane must leave Loading within InteractiveHttpTimeout — never hang on blackholed HTTPS
/// or unexpected parse faults.
/// </summary>
public static class MessengerChatLoad
{
    /// <summary>
    /// Per open-chat history GetEntities budget (AC ~10s leave-Loading).
    /// Applied via CancellationToken on LoadMessagesCore only — Messenger HttpClient stays 45s
    /// so contact list / EnsureBag init is not starved by the chat-loading bound.
    /// </summary>
    public static readonly TimeSpan InteractiveHttpTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Fire-and-forget LoadMessages must clear loading for any fault (connectivity or parse),
    /// not only <see cref="Services.ApiExceptionHelper.IsExpectedConnectivityFailure"/>.
    /// </summary>
    public static bool ShouldClearLoadingOnFault(Exception? ex) => ex != null;
}
