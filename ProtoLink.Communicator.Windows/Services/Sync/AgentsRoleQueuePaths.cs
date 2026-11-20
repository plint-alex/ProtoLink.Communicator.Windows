using System.IO;

namespace ProtoLink.Communicator.Windows.Services.Sync;

/// <summary>
/// Force/Sync must not thrash AgentTeam role queues or mailbox queues when MyNotesFolder
/// maps over <c>Protolink.ru/Agents</c> (ops/agents-queue-cloud-map · NFR-SYNC-1 ·
/// ADR inbox-outbox-todo-done-only).
/// Live 1.0.77 FORCE_MID_INVENTORY: open-queue shell skip cleared wave16 stall, but Force still
/// BFS-listed every <c>done/&lt;taskShell&gt;</c> (+ evidence) past 15m — Force also skips done
/// task shells; local done content stays prune-protected.
/// Incident 2026-11-21: role-only exclude left <c>Agents/inbox</c> wipeable — mailbox queues
/// are the same Force class as role queues.
/// </summary>
internal static class AgentsRoleQueuePaths
{
    /// <summary>Open queues AgentTeam mutates live + done (Force throughput).</summary>
    private static readonly string[] ForceExcludedQueues = { "todo", "progress", "hold", "done" };

    /// <summary>Structural human mailboxes under Agents (ADR inbox-outbox-todo-done-only).</summary>
    private static readonly string[] MailboxRoots = { "inbox", "outbox" };

    /// <summary>Only todo+done under inbox/outbox — no progress/hold.</summary>
    private static readonly string[] MailboxExcludedQueues = { "todo", "done" };

    /// <summary>
    /// Skip destructive Force prune for excluded-queue paths, their role/mailbox/Agents ancestors,
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

        return DirectoryContainsAgentsExcludedQueue(full);
    }

    /// <summary>
    /// Skip mkdir-only Force materialize for task/ask shells under role or mailbox queues
    /// (<c>…/Agents/roles/&lt;role&gt;/todo|…/&lt;task&gt;</c>,
    /// <c>…/Agents/inbox|outbox/todo|done/&lt;ask&gt;</c>).
    /// </summary>
    public static bool ShouldSkipFolderMaterialize(string relativePath)
    {
        var parts = SyncPathUtil.Normalize(relativePath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (!parts[i].Equals("Agents", StringComparison.OrdinalIgnoreCase))
                continue;

            if (i + 1 >= parts.Length)
                continue;

            if (parts[i + 1].Equals("roles", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 3 >= parts.Length)
                    return false;
                if (!IsForceExcludedQueue(parts[i + 3]))
                    return false;
                // Under the queue folder (task shell or deeper) — not the queue folder itself.
                return parts.Length >= i + 5;
            }

            if (IsMailboxRoot(parts[i + 1]))
            {
                if (i + 2 >= parts.Length)
                    return false;
                if (!IsMailboxExcludedQueue(parts[i + 2]))
                    return false;
                // Under the mailbox queue (ask shell or deeper).
                return parts.Length >= i + 4;
            }
        }

        return false;
    }

    /// <summary>
    /// True for Agents ancestors and for role queues
    /// (<c>…/Agents/roles/&lt;role&gt;/{todo,progress,hold,done}</c>) or mailbox queues
    /// (<c>…/Agents/inbox|outbox/{todo,done}</c>) and their descendants.
    /// </summary>
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

            if (parts[i + 1].Equals("roles", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 2 >= parts.Length)
                    return true;
                if (i + 3 >= parts.Length)
                    return true;
                return IsForceExcludedQueue(parts[i + 3]);
            }

            if (IsMailboxRoot(parts[i + 1]))
            {
                if (i + 2 >= parts.Length)
                    return true;
                return IsMailboxExcludedQueue(parts[i + 2]);
            }
        }

        return false;
    }

    private static bool IsForceExcludedQueue(string name) =>
        ForceExcludedQueues.Any(q => q.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool IsMailboxRoot(string name) =>
        MailboxRoots.Any(m => m.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool IsMailboxExcludedQueue(string name) =>
        MailboxExcludedQueues.Any(q => q.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool DirectoryContainsAgentsExcludedQueue(string fullDir)
    {
        try
        {
            foreach (var agentsDir in Directory.EnumerateDirectories(
                         fullDir, "Agents", SearchOption.AllDirectories))
            {
                var rolesDir = Path.Combine(agentsDir, "roles");
                if (Directory.Exists(rolesDir))
                {
                    foreach (var roleDir in Directory.EnumerateDirectories(rolesDir))
                    {
                        foreach (var queue in ForceExcludedQueues)
                        {
                            if (Directory.Exists(Path.Combine(roleDir, queue)))
                                return true;
                        }
                    }
                }

                foreach (var mailbox in MailboxRoots)
                {
                    var mailboxDir = Path.Combine(agentsDir, mailbox);
                    if (!Directory.Exists(mailboxDir))
                        continue;
                    // Structural mailbox root itself is prune-protected (incident 2026-11-21).
                    return true;
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
