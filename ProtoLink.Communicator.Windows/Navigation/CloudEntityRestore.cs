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
    /// Returns null when the chain is empty or does not start at CloudRoot
    /// (legacy Code or normalized <see cref="CloudEntityCodes.CloudRoot"/> marker).
    /// </summary>
    public static (CloudEntityRef Entity, IReadOnlyList<CloudBreadcrumbEntry> Breadcrumb)? BuildOpenTarget(
        IReadOnlyList<AncestorNode> chainRootToEntity)
    {
        if (chainRootToEntity.Count == 0) return null;
        if (!CloudEntityClassify.IsCloudRootCode(chainRootToEntity[0].Code)) return null;

        var entityNode = chainRootToEntity[^1];
        var isFile = CloudEntityClassify.IsFile(typeEntityId: null, entityNode.Code);
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
                Name = CloudEntityClassify.IsCloudRootCode(n.Code) ? "Cloud" : n.Name
            });
        }

        Guid? parentId = chainRootToEntity.Count >= 2 ? chainRootToEntity[^2].Id : null;
        var kind = isFile ? CloudEntityKind.File : CloudEntityKind.Folder;
        var name = CloudEntityClassify.IsCloudRootCode(entityNode.Code) ? "Cloud" : entityNode.Name;
        return (new CloudEntityRef(entityNode.Id, kind, name, parentId), breadcrumb);
    }

    public static bool IsCloudFolderOrRoot(string? code) =>
        CloudEntityClassify.IsFolder(typeEntityId: null, code);

    public static bool IsCloudFolderOrRoot(Guid? typeEntityId, string? code) =>
        CloudEntityClassify.IsFolder(typeEntityId, code);

    public static bool IsNavigableCloudCode(string? code) =>
        CloudEntityClassify.IsNavigableCloudEntity(typeEntityId: null, code);

    public static bool IsNavigableCloudEntity(
        Guid? typeEntityId,
        string? code,
        bool isKnownCloudRoot = false) =>
        CloudEntityClassify.IsNavigableCloudEntity(typeEntityId, code, isKnownCloudRoot);

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

    /// <summary>
    /// Sick/cold LastOpen when Api ancestry is unavailable but listing disk has rows for
    /// <paramref name="folderId"/>: open as My Cloud under the hub so listing paint can run.
    /// </summary>
    public static (CloudEntityRef Entity, IReadOnlyList<CloudBreadcrumbEntry> Breadcrumb) BuildOfflineMyCloudOpen(
        Guid folderId,
        string hubName,
        string myCloudName)
    {
        var entity = new CloudEntityRef(folderId, CloudEntityKind.Folder, myCloudName, null);
        var breadcrumb = new[]
        {
            new CloudBreadcrumbEntry { Index = 0, Id = folderId, Name = myCloudName }
        };
        return WithCloudHub(entity, breadcrumb, hubName, myCloudName);
    }
}
