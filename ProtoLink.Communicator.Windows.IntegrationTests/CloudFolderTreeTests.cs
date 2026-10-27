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

    [Fact]
    public void ProjectVisible_StartsCollapsed_HidesGrandchildren()
    {
        var root = Guid.NewGuid();
        var a = Guid.NewGuid();
        var a1 = Guid.NewGuid();
        var map = new Dictionary<Guid, IReadOnlyList<CloudFolderTree.Node>>
        {
            [root] = new[] { new CloudFolderTree.Node(a, "A") },
            [a] = new[] { new CloudFolderTree.Node(a1, "A1") }
        };
        var loaded = new HashSet<Guid> { root, a };
        var expanded = new HashSet<Guid> { root }; // root open → children visible, A collapsed
        var rows = CloudFolderTree.ProjectVisible(
            new CloudFolderTree.Node(root, "Root"),
            map,
            expanded,
            loaded,
            selectedId: a);

        Assert.Equal(2, rows.Count);
        Assert.Equal(root, rows[0].Id);
        Assert.True(rows[0].IsExpanded);
        Assert.Equal(a, rows[1].Id);
        Assert.False(rows[1].IsExpanded);
        Assert.True(rows[1].CanExpand);
        Assert.DoesNotContain(rows, r => r.Id == a1);
    }

    [Fact]
    public void ProjectVisible_ExpandRevealsChildren()
    {
        var root = Guid.NewGuid();
        var a = Guid.NewGuid();
        var a1 = Guid.NewGuid();
        var map = new Dictionary<Guid, IReadOnlyList<CloudFolderTree.Node>>
        {
            [root] = new[] { new CloudFolderTree.Node(a, "A") },
            [a] = new[] { new CloudFolderTree.Node(a1, "A1") }
        };
        var loaded = new HashSet<Guid> { root, a };
        var expanded = new HashSet<Guid> { root, a };
        var rows = CloudFolderTree.ProjectVisible(
            new CloudFolderTree.Node(root, "Root"),
            map,
            expanded,
            loaded,
            selectedId: null);

        Assert.Equal(3, rows.Count);
        Assert.Equal(a1, rows[2].Id);
        Assert.Equal(2, rows[2].Depth);
    }

    [Fact]
    public void ProjectVisible_UnloadedParentStaysExpandable()
    {
        var root = Guid.NewGuid();
        var a = Guid.NewGuid();
        var map = new Dictionary<Guid, IReadOnlyList<CloudFolderTree.Node>>
        {
            [root] = new[] { new CloudFolderTree.Node(a, "A") }
        };
        var loaded = new HashSet<Guid> { root }; // A not fetched yet
        var expanded = new HashSet<Guid> { root };
        var rows = CloudFolderTree.ProjectVisible(
            new CloudFolderTree.Node(root, "Root"),
            map,
            expanded,
            loaded,
            selectedId: null);

        Assert.Equal(2, rows.Count);
        Assert.True(rows[1].CanExpand);
        Assert.False(rows[1].IsExpanded);
    }

    [Fact]
    public void ProjectVisible_LoadedEmptyIsLeaf()
    {
        var root = Guid.NewGuid();
        var a = Guid.NewGuid();
        var map = new Dictionary<Guid, IReadOnlyList<CloudFolderTree.Node>>
        {
            [root] = new[] { new CloudFolderTree.Node(a, "A") },
            [a] = Array.Empty<CloudFolderTree.Node>()
        };
        var loaded = new HashSet<Guid> { root, a };
        var expanded = new HashSet<Guid> { root };
        var rows = CloudFolderTree.ProjectVisible(
            new CloudFolderTree.Node(root, "Root"),
            map,
            expanded,
            loaded,
            selectedId: null);

        Assert.False(rows[1].CanExpand);
    }

    [Fact]
    public void CollapseBranch_RemovesDescendantsFromExpanded()
    {
        var root = Guid.NewGuid();
        var a = Guid.NewGuid();
        var a1 = Guid.NewGuid();
        var map = new Dictionary<Guid, IReadOnlyList<CloudFolderTree.Node>>
        {
            [root] = new[] { new CloudFolderTree.Node(a, "A") },
            [a] = new[] { new CloudFolderTree.Node(a1, "A1") },
            [a1] = Array.Empty<CloudFolderTree.Node>()
        };
        var expanded = new HashSet<Guid> { root, a, a1 };
        CloudFolderTree.CollapseBranch(a, map, expanded);
        Assert.Contains(root, expanded);
        Assert.DoesNotContain(a, expanded);
        Assert.DoesNotContain(a1, expanded);
    }
}
