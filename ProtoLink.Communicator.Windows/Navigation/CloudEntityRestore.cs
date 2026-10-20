using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>AT-C7 restore decision and breadcrumb assembly for Cloud cold start (offline-testable).</summary>
public static class CloudEntityRestore
{
    public enum Decision
    {
        OpenRoot,
        OpenEntity
    }

    /// <summary>Missing or gone id → Cloud root; present → open that entity (no error dialog).</summary>
    public static Decision Decide(Guid? lastOpenEntityId, bool entityExists) =>
        lastOpenEntityId is null || !entityExists ? Decision.OpenRoot : Decision.OpenEntity;

    public sealed record AncestorNode(Guid Id, string Code, string Name);

    /// <summary>
    /// Build open target from an ancestor chain ordered CloudRoot → … → entity (inclusive).
    /// Returns null when the chain is empty or does not start at CloudRoot.
    /// </summary>
    public static (CloudEntityRef Entity, IReadOnlyList<CloudBreadcrumbEntry> Breadcrumb)? BuildOpenTarget(
        IReadOnlyList<AncestorNode> chainRootToEntity)
    {
        if (chainRootToEntity.Count == 0) return null;
        if (chainRootToEntity[0].Code != CloudEntityCodes.CloudRoot) return null;

        var entityNode = chainRootToEntity[^1];
        var isFile = entityNode.Code == CloudEntityCodes.CloudFile;
        var folderNodes = isFile
            ? chainRootToEntity.Take(chainRootToEntity.Count - 1).ToList()
            : chainRootToEntity.ToList();
        if (folderNodes.Count == 0) return null;

        var breadcrumb = new List<CloudBreadcrumbEntry>(folderNodes.Count);
        for (var i = 0; i < folderNodes.Count; i++)
        {
            var n = folderNodes[i];
            breadcrumb.Add(new CloudBreadcrumbEntry
            {
                Index = i,
                Id = n.Id,
                Name = n.Code == CloudEntityCodes.CloudRoot ? "Cloud" : n.Name
            });
        }

        Guid? parentId = chainRootToEntity.Count >= 2 ? chainRootToEntity[^2].Id : null;
        var kind = isFile ? CloudEntityKind.File : CloudEntityKind.Folder;
        var name = entityNode.Code == CloudEntityCodes.CloudRoot ? "Cloud" : entityNode.Name;
        return (new CloudEntityRef(entityNode.Id, kind, name, parentId), breadcrumb);
    }

    public static bool IsCloudFolderOrRoot(string? code) =>
        code is CloudEntityCodes.CloudRoot or CloudEntityCodes.CloudFolder;

    public static bool IsNavigableCloudCode(string? code) =>
        code is CloudEntityCodes.CloudRoot or CloudEntityCodes.CloudFolder or CloudEntityCodes.CloudFile;

    /// <summary>
    /// Prepend the Cloud hub so My Cloud stays a sibling of Shared with me after cold restore (AT-S17).
    /// </summary>
    public static (CloudEntityRef Entity, IReadOnlyList<CloudBreadcrumbEntry> Breadcrumb) WithCloudHub(
        CloudEntityRef entity,
        IReadOnlyList<CloudBreadcrumbEntry> breadcrumb,
        string hubName,
        string myCloudName)
    {
        var crumbs = breadcrumb.Select(b => new CloudBreadcrumbEntry { Id = b.Id, Name = b.Name }).ToList();
        if (crumbs.Count > 0)
            crumbs[0] = new CloudBreadcrumbEntry { Id = crumbs[0].Id, Name = myCloudName };

        crumbs.Insert(0, new CloudBreadcrumbEntry { Id = CloudVirtualNodes.HubId, Name = hubName });
        for (var i = 0; i < crumbs.Count; i++)
            crumbs[i].Index = i;

        var resultEntity = entity;
        if (crumbs.Count >= 2 && entity.Id == crumbs[1].Id)
            resultEntity = entity with { ParentId = CloudVirtualNodes.HubId, Name = myCloudName };

        return (resultEntity, crumbs);
    }
}
