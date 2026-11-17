using ProtoLink.Communicator.Windows.Services.Sync;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public sealed class SameNamedCloudSiblingTests
{
    [Fact]
    public void PreferExisting_SameNameFile_ReusesNewest()
    {
        var parent = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var older = new RemoteEntryInfo
        {
            Id = Guid.Parse("11111111-1111-4111-8111-111111111111"),
            ParentId = parent,
            Name = "index.html",
            IsFolder = false,
            UpdateTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var newer = new RemoteEntryInfo
        {
            Id = Guid.Parse("22222222-2222-4222-8222-222222222222"),
            ParentId = parent,
            Name = "index.html",
            IsFolder = false,
            UpdateTime = new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc)
        };
        var other = new RemoteEntryInfo
        {
            Id = Guid.Parse("33333333-3333-4333-8333-333333333333"),
            ParentId = parent,
            Name = "readme.md",
            IsFolder = false,
            UpdateTime = DateTime.UtcNow
        };

        var pick = SameNamedCloudSibling.PreferExisting(
            parent, "index.html", isFolder: false, new[] { older, newer, other });

        Assert.NotNull(pick);
        Assert.Equal(newer.Id, pick!.Id);
    }

    [Fact]
    public void PreferExisting_SameNameFolder_Reuses()
    {
        var parent = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var folder = new RemoteEntryInfo
        {
            Id = Guid.Parse("44444444-4444-4444-8444-444444444444"),
            ParentId = parent,
            Name = "Agents",
            IsFolder = true,
            UpdateTime = DateTime.UtcNow
        };
        var fileTwin = new RemoteEntryInfo
        {
            Id = Guid.Parse("55555555-5555-4555-8555-555555555555"),
            ParentId = parent,
            Name = "Agents",
            IsFolder = false,
            UpdateTime = DateTime.UtcNow
        };

        var pick = SameNamedCloudSibling.PreferExisting(
            parent, "agents", isFolder: true, new[] { folder, fileTwin });

        Assert.NotNull(pick);
        Assert.Equal(folder.Id, pick!.Id);
        Assert.True(pick.IsFolder);
    }

    [Fact]
    public void PreferExisting_SkipsSyntheticFileName()
    {
        var parent = Guid.NewGuid();
        var id = Guid.Parse("66666666-6666-4666-8666-666666666666");
        var synthetic = new RemoteEntryInfo
        {
            Id = id,
            ParentId = parent,
            Name = $"file-{id:N}",
            IsFolder = false,
            UpdateTime = DateTime.UtcNow
        };

        var pick = SameNamedCloudSibling.PreferExisting(
            parent, $"file-{id:N}", isFolder: false, new[] { synthetic });

        Assert.Null(pick);
    }

    [Fact]
    public void PreferExisting_NoMatch_ReturnsNull()
    {
        var parent = Guid.NewGuid();
        var other = new RemoteEntryInfo
        {
            Id = Guid.NewGuid(),
            ParentId = parent,
            Name = "other.txt",
            IsFolder = false
        };

        Assert.Null(SameNamedCloudSibling.PreferExisting(
            parent, "index.html", isFolder: false, new[] { other }));
    }
}
