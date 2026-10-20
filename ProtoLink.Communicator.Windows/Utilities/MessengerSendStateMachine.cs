using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Pure transitions for media send (client-contract §3). Offline-testable.
/// Retry after entity create resumes at upload and reuses the same CloudFile id.
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
    /// Retry must keep the CloudFile entity id from the failed attempt (no second AddEntity for that file).
    /// </summary>
    public static Guid ResolveEntityIdForRetry(Guid previousEntityId, Guid? newlyCreatedId)
    {
        if (previousEntityId != Guid.Empty)
            return previousEntityId;
        return newlyCreatedId ?? Guid.Empty;
    }
}
