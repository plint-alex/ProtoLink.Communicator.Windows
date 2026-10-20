namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// M3 — whether a Cloud picker row may be selected for send.
/// Sender without write may only pick when the recipient already has read.
/// </summary>
public static class MessengerCloudShareability
{
    public const string DisabledShareSubtitleEn = "You can't share this item";

    /// <summary>
    /// Selectable when the sender can grant (write) or the recipient already has any read grant.
    /// </summary>
    public static bool CanSelect(bool senderHasWrite, bool recipientAlreadyHasRead) =>
        senderHasWrite || recipientAlreadyHasRead;
}
