using System.IO;

namespace ProtoLink.Communicator.Windows.Services.Sync;

/// <summary>
/// Force/Sync must not thrash AgentTeam open role queues when MyNotesFolder maps over
/// <c>Protolink.ru/Agents</c> (ops/agents-queue-cloud-map · NFR-SYNC-1).
/// </summary>
internal static class AgentsRoleQueuePaths
{
    private static readonly string[] OpenQueues = { "todo", "progress", "hold" };

    /// <summary>
    /// Skip destructive Force prune for open-queue paths, their role/Agents ancestors,
    /// and any directory that contains such a queue on disk (e.g. <c>Protolink.ru</c>).
    /// </summary>
    public static bool IsForcePruneProtected(string relativePath, string? localRootFullPath = null)
    {
        if (MatchesOpenQueueOrAgentsRolesAncestor(relativePath))
            return true;

        if (string.IsNullOrEmpty(localRootFullPath))
            return false;

        var full = ToFull(localRootFullPath, relativePath);
        if (!Directory.Exists(full))
            return false;

        return DirectoryContainsAgentsOpenQueue(full);
    }

    /// <summary>
    /// Skip mkdir-only Force materialize for task shells under open queues
    /// (<c>…/Agents/roles/&lt;role&gt;/todo|progress|hold/&lt;task&gt;</c>).
    /// </summary>
    public static bool ShouldSkipFolderMaterialize(string relativePath)
    {
        var parts = SyncPathUtil.Normalize(relativePath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (!parts[i].Equals("Agents", StringComparison.OrdinalIgnoreCase))
                continue;
            if (i + 1 >= parts.Length
                || !parts[i + 1].Equals("roles", StringComparison.OrdinalIgnoreCase))
                continue;
            if (i + 3 >= parts.Length)
                return false;
            if (!IsOpenQueue(parts[i + 3]))
                return false;
            // Under the queue folder (task shell or deeper) — not the queue folder itself.
            return parts.Length >= i + 5;
        }

        return false;
    }

    internal static bool MatchesOpenQueueOrAgentsRolesAncestor(string relativePath)
    {
        var parts = SyncPathUtil.Normalize(relativePath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (!parts[i].Equals("Agents", StringComparison.OrdinalIgnoreCase))
                continue;
            if (i + 1 >= parts.Length)
                return true;
            if (!parts[i + 1].Equals("roles", StringComparison.OrdinalIgnoreCase))
                continue;
            if (i + 2 >= parts.Length)
                return true;
            if (i + 3 >= parts.Length)
                return true;
            return IsOpenQueue(parts[i + 3]);
        }

        return false;
    }

    private static bool IsOpenQueue(string name) =>
        OpenQueues.Any(q => q.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool DirectoryContainsAgentsOpenQueue(string fullDir)
    {
        try
        {
            foreach (var agentsDir in Directory.EnumerateDirectories(
                         fullDir, "Agents", SearchOption.AllDirectories))
            {
                var rolesDir = Path.Combine(agentsDir, "roles");
                if (!Directory.Exists(rolesDir))
                    continue;
                foreach (var roleDir in Directory.EnumerateDirectories(rolesDir))
                {
                    foreach (var queue in OpenQueues)
                    {
                        if (Directory.Exists(Path.Combine(roleDir, queue)))
                            return true;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Soft-fail: prefer keep over wipe when we cannot inspect (NFR-SYNC-1).
            return true;
        }

        return false;
    }

    private static string ToFull(string localRoot, string relativePath)
    {
        var norm = SyncPathUtil.Normalize(relativePath);
        if (string.IsNullOrEmpty(norm))
            return localRoot;
        return Path.Combine(
            localRoot,
            norm.Replace('/', Path.DirectorySeparatorChar));
    }
}
