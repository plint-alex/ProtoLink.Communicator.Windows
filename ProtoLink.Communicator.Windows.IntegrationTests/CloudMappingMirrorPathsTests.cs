using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>AT-CFD-6 / NFR-SYNC-1 — local mirror delete soft-fail.</summary>
[Trait("Category", "Offline")]
public class CloudMappingMirrorPathsTests
{
    [Fact]
    public void DeleteLocalMirror_MissingPath_ReturnsTrue()
    {
        var item = new CloudItemViewModel { Id = Guid.NewGuid(), Name = "gone", IsFolder = true };
        Assert.True(CloudMappingMirrorPaths.DeleteLocalMirror(item, null));
        Assert.True(CloudMappingMirrorPaths.DeleteLocalMirror(
            item,
            Path.Combine(Path.GetTempPath(), "pl-cfd-missing-" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void DeleteLocalMirror_ExistingFolder_RemovesTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-cfd-folder-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "child");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "a.txt"), "x");
        var item = new CloudItemViewModel { Id = Guid.NewGuid(), Name = "root", IsFolder = true };

        Assert.True(CloudMappingMirrorPaths.DeleteLocalMirror(item, root));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void DeleteLocalMirror_ExistingFile_RemovesFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "pl-cfd-file-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "x");
        var item = new CloudItemViewModel { Id = Guid.NewGuid(), Name = "f.txt", IsFolder = false };

        Assert.True(CloudMappingMirrorPaths.DeleteLocalMirror(item, path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void GetLocalPathForCloudItem_UnderMappedBreadcrumb_CombinesSegments()
    {
        var mapId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var localRoot = Path.Combine(Path.GetTempPath(), "pl-cfd-map");
        var crumbs = new[]
        {
            new CloudBreadcrumbEntry { Id = mapId, Name = "Mapped", Index = 0 },
            new CloudBreadcrumbEntry { Id = childId, Name = "Child", Index = 1 }
        };
        var mappings = new[]
        {
            new CloudSyncMapping
            {
                CloudFolderId = mapId,
                CloudFolderName = "Mapped",
                LocalPath = localRoot
            }
        };
        var item = new CloudItemViewModel { Id = Guid.NewGuid(), Name = "doc.txt", IsFolder = false };

        var path = CloudMappingMirrorPaths.GetLocalPathForCloudItem(item, crumbs, mappings);
        Assert.Equal(Path.Combine(localRoot, "Child", "doc.txt"), path);
    }

    /// <summary>
    /// AT-CFD-6: deleting a mapped root from My Cloud (parent list) — breadcrumb has no mapping;
    /// local path must resolve from item.Id mapping, not null.
    /// </summary>
    [Fact]
    public void GetLocalPathForCloudItem_MappedRootFromParentList_UsesItemMapping()
    {
        var mapId = Guid.NewGuid();
        var cloudRootId = Guid.NewGuid();
        var localRoot = Path.Combine(Path.GetTempPath(), "pl-cfd-map-root-" + Guid.NewGuid().ToString("N"));
        var crumbs = new[]
        {
            new CloudBreadcrumbEntry { Id = cloudRootId, Name = "My Cloud", Index = 0 }
        };
        var mappings = new[]
        {
            new CloudSyncMapping
            {
                CloudFolderId = mapId,
                CloudFolderName = "MappedRoot",
                LocalPath = localRoot
            }
        };
        var item = new CloudItemViewModel { Id = mapId, Name = "MappedRoot", IsFolder = true };

        var path = CloudMappingMirrorPaths.GetLocalPathForCloudItem(item, crumbs, mappings);
        Assert.Equal(localRoot, path);
    }

    [Fact]
    public void GetLocalPathForCloudItem_MappedRootFromParentList_ThenDeleteRemovesTree()
    {
        var mapId = Guid.NewGuid();
        var cloudRootId = Guid.NewGuid();
        var localRoot = Path.Combine(Path.GetTempPath(), "pl-cfd-map-del-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(localRoot, "nested"));
        File.WriteAllText(Path.Combine(localRoot, "nested", "a.txt"), "x");
        var crumbs = new[]
        {
            new CloudBreadcrumbEntry { Id = cloudRootId, Name = "My Cloud", Index = 0 }
        };
        var mappings = new[]
        {
            new CloudSyncMapping
            {
                CloudFolderId = mapId,
                CloudFolderName = "MappedRoot",
                LocalPath = localRoot
            }
        };
        var item = new CloudItemViewModel { Id = mapId, Name = "MappedRoot", IsFolder = true };

        var path = CloudMappingMirrorPaths.GetLocalPathForCloudItem(item, crumbs, mappings);
        Assert.True(CloudMappingMirrorPaths.DeleteLocalMirror(item, path));
        Assert.False(Directory.Exists(localRoot));
    }
}
