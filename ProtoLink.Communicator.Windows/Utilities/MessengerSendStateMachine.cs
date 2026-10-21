using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Pure transitions for media send (client-contract §3 / §9). Offline-testable.
/// Cycle-2 atomic send: Failed → Retry is a full re-send for new uploads (Api assigns CloudFile ids).
/// Cloud picks still reuse their existing entity id via <see cref="ResolveEntityIdForRetry"/>.
/// </summary>
public static class MessengerSendStateMachine
{
    public static MessageDeliveryStatus OnBubbleCreated(bool hasAttachments) =>
        hasAttachments ? MessageDeliveryStatus.Uploading : MessageDeliveryStatus.Sending;

    public static MessageDeliveryStatus OnUploadFinished() => MessageDeliveryStatus.Sending;

    public static MessageDeliveryStatus OnMessageCommitted() => MessageDeliveryStatus.Sent;

    public static MessageDeliveryStatus OnFailure() => MessageDeliveryStatus.Failed;

    public static MessageDeliveryStatus OnRetry() => MessageDeliveryStatus.Uploading;

    /// <summary>
    /// Cloud-pick / legacy: keep the CloudFile entity id from the failed attempt.
    /// New atomic uploads clear the id in <see cref="MessengerAtomicSend.PrepareDraftsForAtomicRetry"/>.
    /// </summary>
    public static Guid ResolveEntityIdForRetry(Guid previousEntityId, Guid? newlyCreatedId)
    {
        if (previousEntityId != Guid.Empty)
            return previousEntityId;
        return newlyCreatedId ?? Guid.Empty;
    }
}
