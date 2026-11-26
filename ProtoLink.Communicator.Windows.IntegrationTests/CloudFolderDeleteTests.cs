using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Sync;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>E10 / AT-CFD offline: recursive delete order + sync-meta purge so Soft Ready cannot resurrect.</summary>
[Trait("Category", "Offline")]
public class CloudFolderDeleteTests
{
    [Fact]
    public async Task DeleteFolderRecursive_DepthFirst_FilesThenFoldersThenSelf()
    {
        var order = new List<Guid>();
        var root = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var file1 = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var sub = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        var file2 = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
        var junk = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
        var tree = new Dictionary<Guid, List<CloudFolderDelete.ChildRef>>
        {
            [root] =
            [
                new(file1, CloudEntityCodes.CloudFile),
                new(sub, CloudEntityCodes.CloudFolder),
                new(junk, "SomeOtherCode"),
                new(root, CloudEntityCodes.CloudFolder) // self-id must be skipped
            ],
            [sub] = [new(file2, CloudEntityCodes.CloudFile)]
        };

        var deleted = new HashSet<Guid>();
        await CloudFolderDelete.DeleteFolderRecursiveAsync(
            root,
            id => Task.FromResult<IReadOnlyList<CloudFolderDelete.ChildRef>>(
                tree.TryGetValue(id, out var kids) ? kids : Array.Empty<CloudFolderDelete.ChildRef>()),
            id =>
            {
                order.Add(id);
                return Task.CompletedTask;
            },
            deleted);

        Assert.Equal(new[] { file1, file2, sub, root }, order);
        Assert.DoesNotContain(junk, order);
        Assert.Equal(new HashSet<Guid> { file1, file2, sub, root }, deleted);
    }

    [Fact]
    public async Task DeleteFolderRecursive_TypeEntityId_DisplayNameCode()
    {
        // Code=name: classifiers must use TypeEntityId (legacy Code discriminators absent).
        var order = new List<Guid>();
        var root = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var file1 = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var sub = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        var file2 = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
        var tree = new Dictionary<Guid, List<CloudFolderDelete.ChildRef>>
        {
            [root] =
            [
                new(file1, "notes.txt", CloudEntityCodes.TypeCloudFileId),
                new(sub, "Nested", CloudEntityCodes.TypeCloudFolderId),
            ],
            [sub] = [new(file2, "deep.bin", CloudEntityCodes.TypeCloudFileId)]
        };

        var deleted = new HashSet<Guid>();
        await CloudFolderDelete.DeleteFolderRecursiveAsync(
            root,
            id => Task.FromResult<IReadOnlyList<CloudFolderDelete.ChildRef>>(
                tree.TryGetValue(id, out var kids) ? kids : Array.Empty<CloudFolderDelete.ChildRef>()),
            id =>
            {
                order.Add(id);
                return Task.CompletedTask;
            },
            deleted);

        Assert.Equal(new[] { file1, file2, sub, root }, order);
        Assert.Equal(new HashSet<Guid> { file1, file2, sub, root }, deleted);
    }

    [Fact]
    public async Task DeleteFolderRecursive_NestedDepth_LeafFirstNoPromoteOrder()
    {
        // Deeper than shallow AT-CFD 1.0.124 fixtures: root → mid → leafFolder → file.
        var order = new List<Guid>();
        var root = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var mid = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var leafFolder = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        var leafFile = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
        var tree = new Dictionary<Guid, List<CloudFolderDelete.ChildRef>>
        {
            [root] = [new(mid, CloudEntityCodes.CloudFolder)],
            [mid] = [new(leafFolder, CloudEntityCodes.CloudFolder)],
            [leafFolder] = [new(leafFile, CloudEntityCodes.CloudFile)]
        };

        await CloudFolderDelete.DeleteFolderRecursiveAsync(
            root,
            id => Task.FromResult<IReadOnlyList<CloudFolderDelete.ChildRef>>(
                tree.TryGetValue(id, out var kids) ? kids : Array.Empty<CloudFolderDelete.ChildRef>()),
            id =>
            {
                order.Add(id);
                return Task.CompletedTask;
            });

        Assert.Equal(new[] { leafFile, leafFolder, mid, root }, order);
    }

    [Fact]
    public async Task DeleteFolderRecursive_ListChildrenThrows_DoesNotDeleteParent()
    {
        var root = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var order = new List<Guid>();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CloudFolderDelete.DeleteFolderRecursiveAsync(
                root,
                _ => throw new InvalidOperationException("simulated GetEntities 504"),
                id =>
                {
                    order.Add(id);
                    return Task.CompletedTask;
                }));
        Assert.Empty(order);
    }

    [Fact]
    public async Task DeleteFolderRecursive_ListChildrenNull_DoesNotDeleteParent()
    {
        var root = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var order = new List<Guid>();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CloudFolderDelete.DeleteFolderRecursiveAsync(
                root,
                _ => Task.FromResult<IReadOnlyList<CloudFolderDelete.ChildRef>>(null!),
                id =>
                {
                    order.Add(id);
                    return Task.CompletedTask;
                }));
        Assert.Contains("promote", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(order);
    }

    [Fact]
    public void RemoveByRemoteIds_DropsSoftReadyChildren()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ProtoLinkTests", "cfd-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new JsonSyncMetadataStore(dir);
            var parent = Guid.Parse("11111111-1111-4111-8111-111111111111");
            var keep = Guid.Parse("22222222-2222-4222-8222-222222222222");
            var gone = Guid.Parse("33333333-3333-4333-8333-333333333333");
            store.Upsert(new SyncItemMeta
            {
                MappingId = "map1",
                RemoteId = keep,
                ParentRemoteId = parent,
                RelativePath = "Keep",
                IsFolder = true
            });
            store.Upsert(new SyncItemMeta
            {
                MappingId = "map1",
                RemoteId = gone,
                ParentRemoteId = parent,
                RelativePath = "Gone",
                IsFolder = true
            });

            Assert.Equal(2, store.FindChildrenByParentRemoteId(parent).Count);
            Assert.Equal(1, store.RemoveByRemoteIds(new[] { gone }));
            var kids = store.FindChildrenByParentRemoteId(parent);
            Assert.Single(kids);
            Assert.Equal(keep, kids[0].RemoteId);

            var rows = CloudInteractiveListing.BuildSoftReadyRows(parent, kids, maxRows: 80);
            Assert.Single(rows);
            Assert.Equal(keep, rows[0].Id);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* NFR-TEST-1 best-effort */ }
        }
    }
}
