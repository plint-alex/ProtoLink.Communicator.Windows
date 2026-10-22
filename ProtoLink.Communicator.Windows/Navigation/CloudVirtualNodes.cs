namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// Client-only Cloud hub nodes (AT-S17 / C6): My Cloud + Shared with me + Recent as siblings.
/// Ids are fixed and never sent to the API as parent/entity ids.
/// </summary>
public static class CloudVirtualNodes
{
    public static readonly Guid HubId = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    public static readonly Guid SharedWithMeId = Guid.Parse("a2222222-2222-4222-8222-222222222222");
    public static readonly Guid RecentId = Guid.Parse("a3333333-3333-4333-8333-333333333333");

    public static bool IsVirtual(Guid id) =>
        id == HubId || id == SharedWithMeId || id == RecentId;
}
