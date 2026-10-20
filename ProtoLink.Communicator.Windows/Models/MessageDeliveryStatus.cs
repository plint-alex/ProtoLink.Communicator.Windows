namespace ProtoLink.Communicator.Windows.Models;

/// <summary>Outgoing delivery / upload state (messenger-media M7). Failed is in-memory only.</summary>
public enum MessageDeliveryStatus
{
    Uploading,
    Sending,
    Sent,
    Read,
    Failed
}
