namespace ProtoLink.Communicator.Windows.Models;

public class GetEntitiesContract
{
    public Guid[]? Ids { get; set; }
    public Guid[]? ParentIds { get; set; }
    public Guid[]? IdsToFindParents { get; set; }
    public int Skip { get; set; }
    public int? Take { get; set; }
    public bool IncludeValues { get; set; }

    /// <summary>
    /// Optional conversation scope for Message entities: 1 id (self-chat) or 2 ids (pair).
    /// Server filters sender:/receiver: both directions, newest-first. Omit for legacy global list.
    /// </summary>
    public Guid[]? ConversationUserIds { get; set; }

    /// <summary>S6 Shared with me: list caller's direct share roots (no parentIds).</summary>
    public bool SharedWithMe { get; set; }
}
