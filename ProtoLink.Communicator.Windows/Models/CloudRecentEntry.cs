namespace ProtoLink.Communicator.Windows.Models;

/// <summary>One durable Recent (C6) open — local MRU beside E4 session history.</summary>
public sealed record CloudRecentEntry(
    Guid EntityId,
    string Name,
    bool IsFolder,
    DateTime OpenedUtc);
