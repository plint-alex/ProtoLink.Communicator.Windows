using ProtoLink.Communicator.Windows.Models.Share;

namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Pure Notes-tree Share helpers (notes-tree-share AC-NS / AT-S47…S50).
/// External-share = people beyond creator and/or live guest links and/or public grant.
/// </summary>
public static class NotesTreeShare
{
    /// <summary>
    /// True when the entity has sharing beyond “Only you” (creator-only, no live guest links).
    /// </summary>
    public static bool HasExternalShare(
        IEnumerable<ShareGrantResult>? grants,
        int liveGuestLinkCount,
        Guid? currentUserId)
    {
        if (liveGuestLinkCount > 0)
            return true;
        if (grants == null)
            return false;

        var otherUserCount = 0;
        var selfOnlyUserSeen = false;
        foreach (var g in grants)
        {
            if (!ShareAddPeople.IsPeopleListPrincipal(g.PrincipalKind))
                continue;

            var kind = g.PrincipalKind ?? "";
            if (kind.Equals("public", StringComparison.OrdinalIgnoreCase)
                || kind.Equals("group", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!kind.Equals("user", StringComparison.OrdinalIgnoreCase))
                continue;

            if (currentUserId is { } me)
            {
                if (g.PermissionForId != me)
                    return true;
                selfOnlyUserSeen = true;
            }
            else
            {
                otherUserCount++;
            }
        }

        // Without knowing current user: 2+ user grants ⇒ shared; single user ⇒ Only you.
        if (currentUserId is null && otherUserCount > 1)
            return true;

        _ = selfOnlyUserSeen;
        return false;
    }
}
