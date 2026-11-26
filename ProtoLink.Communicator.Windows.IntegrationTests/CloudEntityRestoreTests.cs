using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>AT-C7 restore decision + breadcrumb assembly (offline).</summary>
[Trait("Category", "Offline")]
public class CloudEntityRestoreTests
{
    [Fact]
    public void Decide_NullId_OpensRoot()
    {
        Assert.Equal(CloudEntityRestore.Decision.OpenRoot, CloudEntityRestore.Decide(null, entityExists: true));
        Assert.Equal(CloudEntityRestore.Decision.OpenRoot, CloudEntityRestore.Decide(null, entityExists: false));
    }

    [Fact]
    public void Decide_IdGone_OpensRoot()
    {
        Assert.Equal(
            CloudEntityRestore.Decision.OpenRoot,
            CloudEntityRestore.Decide(Guid.NewGuid(), entityExists: false));
    }

    [Fact]
    public void Decide_IdPresent_OpensEntity()
    {
        Assert.Equal(
            CloudEntityRestore.Decision.OpenEntity,
            CloudEntityRestore.Decide(Guid.NewGuid(), entityExists: true));
    }

    [Fact]
    public void BuildOpenTarget_FolderChain_SetsBreadcrumbAndParent()
    {
        var root = Guid.NewGuid();
        var folderA = Guid.NewGuid();
        var folderB = Guid.NewGuid();
        var chain = new[]
        {
            new CloudEntityRestore.AncestorNode(root, CloudEntityCodes.CloudRoot, "Cloud"),
            new CloudEntityRestore.AncestorNode(folderA, CloudEntityCodes.CloudFolder, "A"),
            new CloudEntityRestore.AncestorNode(folderB, CloudEntityCodes.CloudFolder, "B")
        };

        var result = CloudEntityRestore.BuildOpenTarget(chain);
        Assert.NotNull(result);
        var (entity, breadcrumb) = result.Value;
        Assert.Equal(folderB, entity.Id);
        Assert.Equal(CloudEntityKind.Folder, entity.Kind);
        Assert.Equal("B", entity.Name);
        Assert.Equal(folderA, entity.ParentId);
        Assert.Equal(3, breadcrumb.Count);
        Assert.Equal(root, breadcrumb[0].Id);
        Assert.Equal("Cloud", breadcrumb[0].Name);
        Assert.Equal(folderB, breadcrumb[2].Id);
    }

    [Fact]
    public void BuildOpenTarget_File_BreadcrumbStopsAtParentFolder()
    {
        var root = Guid.NewGuid();
        var folder = Guid.NewGuid();
        var file = Guid.NewGuid();
        var chain = new[]
        {
            new CloudEntityRestore.AncestorNode(root, CloudEntityCodes.CloudRoot, "Cloud"),
            new CloudEntityRestore.AncestorNode(folder, CloudEntityCodes.CloudFolder, "Docs"),
            new CloudEntityRestore.AncestorNode(file, CloudEntityCodes.CloudFile, "readme.txt")
        };

        var result = CloudEntityRestore.BuildOpenTarget(chain);
        Assert.NotNull(result);
        var (entity, breadcrumb) = result.Value;
        Assert.Equal(file, entity.Id);
        Assert.Equal(CloudEntityKind.File, entity.Kind);
        Assert.Equal(folder, entity.ParentId);
        Assert.Equal(2, breadcrumb.Count);
        Assert.Equal(folder, breadcrumb[1].Id);
        Assert.DoesNotContain(breadcrumb, b => b.Id == file);
    }

    [Fact]
    public void BuildOpenTarget_WithoutCloudRoot_ReturnsNull()
    {
        var folder = Guid.NewGuid();
        var chain = new[]
        {
            new CloudEntityRestore.AncestorNode(folder, CloudEntityCodes.CloudFolder, "Orphan")
        };
        Assert.Null(CloudEntityRestore.BuildOpenTarget(chain));
    }

    [Fact]
    public void BuildOfflineMyCloudOpen_MyCloudOnly_NoHub_E9f()
    {
        var myCloud = Guid.Parse("8aec679c-a389-46b9-8105-8a5b238ee979");
        var (entity, crumbs) = CloudEntityRestore.BuildOfflineMyCloudOpen(myCloud, "Cloud", "My Cloud");
        Assert.Equal(myCloud, entity.Id);
        Assert.Equal(CloudEntityKind.Folder, entity.Kind);
        Assert.Equal("My Cloud", entity.Name);
        Assert.Null(entity.ParentId);
        Assert.Single(crumbs);
        Assert.Equal(myCloud, crumbs[0].Id);
        Assert.Equal("My Cloud", crumbs[0].Name);
        Assert.False(CloudEntityRestore.IsHubLanding(crumbs[0].Id));
    }

    [Fact]
    public void ColdOpenFolderId_IsMyCloud_NotHub_E9f()
    {
        var myCloud = Guid.NewGuid();
        Assert.Equal(myCloud, CloudEntityRestore.ColdOpenFolderId(myCloud));
        Assert.False(CloudEntityRestore.IsHubLanding(CloudEntityRestore.ColdOpenFolderId(myCloud)));
        Assert.True(CloudEntityRestore.IsHubLanding(CloudVirtualNodes.HubId));
    }

    [Fact]
    public void WithMyCloudRootLabel_RenamesRoot_NoHubCrumb_E9f()
    {
        var root = Guid.NewGuid();
        var folder = Guid.NewGuid();
        var entity = new CloudEntityRef(folder, CloudEntityKind.Folder, "Docs", root);
        var crumbs = new[]
        {
            new CloudBreadcrumbEntry { Id = root, Name = "Cloud", Index = 0 },
            new CloudBreadcrumbEntry { Id = folder, Name = "Docs", Index = 1 }
        };

        var (opened, trail) = CloudEntityRestore.WithMyCloudRootLabel(entity, crumbs, "My Cloud");
        Assert.Equal(2, trail.Count);
        Assert.Equal(root, trail[0].Id);
        Assert.Equal("My Cloud", trail[0].Name);
        Assert.Equal(folder, trail[1].Id);
        Assert.DoesNotContain(trail, b => b.Id == CloudVirtualNodes.HubId);
        Assert.Equal(folder, opened.Id);
        Assert.Equal(root, opened.ParentId);
    }
}
