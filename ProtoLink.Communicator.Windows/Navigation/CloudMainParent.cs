using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>AT-PC5 — pick MainParent axis parent (same rule as web photos-nav-helpers pickParentEntity).</summary>
public static class CloudMainParent
{
    /// <summary>
    /// Prefer the parent whose id equals <paramref name="mainParentId"/>; else first navigable Cloud folder/root;
    /// else first row.
    /// </summary>
    public static GetEntitiesResult? PickParent(
        Guid? mainParentId,
        IReadOnlyList<GetEntitiesResult> parents)
    {
        if (parents == null || parents.Count == 0) return null;

        if (mainParentId is { } mp && mp != Guid.Empty)
        {
            foreach (var p in parents)
            {
                if (p.Id == mp) return p;
            }
        }

        foreach (var p in parents)
        {
            if (CloudEntityRestore.IsCloudFolderOrRoot(p.Code)) return p;
        }

        return parents[0];
    }
}
