using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>AT-S17 / AT-S42 Shared with me scope + UX §7 strings (offline).</summary>
[Trait("Category", "Offline")]
public class CloudSharedWithMeScopeTests
{
    [Fact]
    public void IsInSharedWithMeScope_True_WhenBreadcrumbContainsSharedNode()
    {
        var ids = new[]
        {
            CloudVirtualNodes.HubId,
            CloudVirtualNodes.SharedWithMeId,
            Guid.NewGuid()
        };
        Assert.True(CloudSharedScope.IsInSharedWithMeScope(ids));
    }

    [Fact]
    public void IsInSharedWithMeScope_False_UnderMyCloud()
    {
        var ids = new[] { CloudVirtualNodes.HubId, Guid.NewGuid() };
        Assert.False(CloudSharedScope.IsInSharedWithMeScope(ids));
    }

    [Fact]
    public void VirtualNodeIds_AreDistinctAndFlagged()
    {
        Assert.NotEqual(CloudVirtualNodes.HubId, CloudVirtualNodes.SharedWithMeId);
        Assert.True(CloudVirtualNodes.IsVirtual(CloudVirtualNodes.HubId));
        Assert.True(CloudVirtualNodes.IsVirtual(CloudVirtualNodes.SharedWithMeId));
        Assert.False(CloudVirtualNodes.IsVirtual(Guid.NewGuid()));
    }

    [Fact]
    public void SharedWithMe_Strings_EnRuParity()
    {
        Assert.Equal("Shared with me", ShareSheetStrings.SharedWithMe("en"));
        Assert.Equal("Доступные мне", ShareSheetStrings.SharedWithMe("ru-RU"));
        Assert.Equal("My Cloud", ShareSheetStrings.MyCloud("en-US"));
        Assert.Equal("Моё облако", ShareSheetStrings.MyCloud("ru"));

        Assert.Equal(
            "Nothing shared with you yet",
            ShareSheetStrings.EmptySharedTitle("en"));
        Assert.Equal(
            "When someone gives you access, it shows up here.",
            ShareSheetStrings.EmptySharedBody("en"));
        Assert.Equal("Вам пока ничего не открыли", ShareSheetStrings.EmptySharedTitle("ru"));
        Assert.Contains("здесь", ShareSheetStrings.EmptySharedBody("ru"), StringComparison.Ordinal);

        var en = ShareSheetStrings.SharedRowSubtitle("en", "Ivan", canWrite: false);
        Assert.Contains("Shared by Ivan", en, StringComparison.Ordinal);
        Assert.Contains("Can view", en, StringComparison.Ordinal);

        var ruEdit = ShareSheetStrings.SharedRowSubtitle("ru", "Анна", canWrite: true);
        Assert.Contains("Поделился(ась) Анна", ruEdit, StringComparison.Ordinal);
        Assert.Contains("Редактирование", ruEdit, StringComparison.Ordinal);

        Assert.Contains("Browse only", ShareSheetStrings.BrowseOnlyHint("en"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("просмотр", ShareSheetStrings.BrowseOnlyHint("ru"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithCloudHub_PrependsHub_AndRenamesMyCloud()
    {
        var root = Guid.NewGuid();
        var folder = Guid.NewGuid();
        var entity = new Models.CloudEntityRef(folder, Models.CloudEntityKind.Folder, "Docs", root);
        var crumbs = new[]
        {
            new Models.CloudBreadcrumbEntry { Id = root, Name = "Cloud", Index = 0 },
            new Models.CloudBreadcrumbEntry { Id = folder, Name = "Docs", Index = 1 }
        };

        var (opened, trail) = CloudEntityRestore.WithCloudHub(entity, crumbs, "Cloud", "My Cloud");
        Assert.Equal(3, trail.Count);
        Assert.Equal(CloudVirtualNodes.HubId, trail[0].Id);
        Assert.Equal("Cloud", trail[0].Name);
        Assert.Equal(root, trail[1].Id);
        Assert.Equal("My Cloud", trail[1].Name);
        Assert.Equal(folder, opened.Id);
        Assert.Equal(root, opened.ParentId);
    }

    [Fact]
    public void WithCloudHub_CloudRootEntity_ParentsToHub()
    {
        var root = Guid.NewGuid();
        var entity = new Models.CloudEntityRef(root, Models.CloudEntityKind.Folder, "Cloud", null);
        var crumbs = new[] { new Models.CloudBreadcrumbEntry { Id = root, Name = "Cloud", Index = 0 } };

        var (opened, trail) = CloudEntityRestore.WithCloudHub(entity, crumbs, "Cloud", "My Cloud");
        Assert.Equal(2, trail.Count);
        Assert.Equal(CloudVirtualNodes.HubId, opened.ParentId);
        Assert.Equal("My Cloud", opened.Name);
    }
}
