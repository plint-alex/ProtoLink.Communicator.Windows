using System.Linq;

namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>AT-S42: Map is browse-only under Shared with me (no download-only sync in cycle 1).</summary>
public static class CloudSharedScope
{
    public static bool IsInSharedWithMeScope(IEnumerable<Guid> breadcrumbIds) =>
        breadcrumbIds.Any(id => id == CloudVirtualNodes.SharedWithMeId);
}
