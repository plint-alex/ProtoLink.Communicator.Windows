using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class PhotosLibraryTests
{
    [Fact]
    public void ParseFolderId_ReadsPhotosfolderPointer()
    {
        var lib = Guid.NewGuid();
        var values = new[]
        {
            new EntityValue { Value = "noise" },
            new EntityValue { Value = PhotosLibrary.FolderPrefix + lib.ToString("D") }
        };
        Assert.Equal(lib, PhotosLibrary.ParseFolderId(values));
    }

    [Fact]
    public void ParseFolderId_Missing_ReturnsNull()
    {
        Assert.Null(PhotosLibrary.ParseFolderId(new[] { new EntityValue { Value = "photoselected:x" } }));
        Assert.Null(PhotosLibrary.ParseFolderId(null));
    }

    [Fact]
    public void ResolveLibraryIdFromUserChildren_FindsPhotosHost()
    {
        var lib = Guid.NewGuid();
        var kids = new[]
        {
            new GetEntitiesResult { Id = Guid.NewGuid(), Code = "Friends" },
            new GetEntitiesResult
            {
                Id = Guid.NewGuid(),
                Code = PhotosLibrary.HostCode,
                Values = new List<EntityValue>
                {
                    new() { Value = PhotosLibrary.FolderPrefix + lib.ToString("D") }
                }
            }
        };
        Assert.Equal(lib, PhotosLibrary.ResolveLibraryIdFromUserChildren(kids));
    }
}
