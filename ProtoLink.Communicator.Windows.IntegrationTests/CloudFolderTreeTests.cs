using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class CloudFolderTreeTests
{
    [Fact]
    public void PickTreeRootId_PrefersPhotosLibraryWhenOnTrail()
    {
        var hub = CloudVirtualNodes.HubId;
        var root = Guid.NewGuid();
        var lib = Guid.NewGuid();
        var day = Guid.NewGuid();
        var crumbs = new (Guid, string?)[]
        {
            (hub, null),
            (root, CloudEntityCodes.CloudRoot),
            (lib, CloudEntityCodes.CloudFolder),
            (day, CloudEntityCodes.CloudFolder)
        };
        Assert.Equal(lib, CloudFolderTree.PickTreeRootId(crumbs, lib, CloudVirtualNodes.IsVirtual));
    }

    [Fact]
    public void PickTreeRootId_PrefersCloudRootWithoutPhotos()
    {
        var hub = CloudVirtualNodes.HubId;
        var root = Guid.NewGuid();
        var folder = Guid.NewGuid();
        var crumbs = new (Guid, string?)[]
        {
            (hub, null),
            (root, CloudEntityCodes.CloudRoot),
            (folder, CloudEntityCodes.CloudFolder)
        };
        Assert.Equal(root, CloudFolderTree.PickTreeRootId(crumbs, photosLibraryId: null, CloudVirtualNodes.IsVirtual));
    }

    [Fact]
    public void Flatten_DepthAndSelection()
    {
        var root = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var map = new Dictionary<Guid, IReadOnlyList<CloudFolderTree.Node>>
        {
            [root] = new[] { new CloudFolderTree.Node(a, "A"), new CloudFolderTree.Node(b, "B") },
            [a] = Array.Empty<CloudFolderTree.Node>(),
            [b] = Array.Empty<CloudFolderTree.Node>()
        };
        var rows = CloudFolderTree.Flatten(new CloudFolderTree.Node(root, "Root"), map, selectedId: b);
        Assert.Equal(3, rows.Count);
        Assert.Equal(0, rows[0].Depth);
        Assert.True(rows[2].Selected);
        Assert.Equal(b, rows[2].Id);
    }

    [Fact]
    public void SelectedFolderId_FileUsesParent()
    {
        var file = Guid.NewGuid();
        var parent = Guid.NewGuid();
        Assert.Equal(parent, CloudFolderTree.SelectedFolderId(isFileOpen: true, file, parent));
        Assert.Equal(file, CloudFolderTree.SelectedFolderId(isFileOpen: false, file, parent));
    }
}
