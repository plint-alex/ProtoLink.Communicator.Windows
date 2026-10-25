using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class CloudMainParentTests
{
    [Fact]
    public void PickParent_PrefersMainParentId()
    {
        var main = Guid.NewGuid();
        var other = Guid.NewGuid();
        var parents = new[]
        {
            new GetEntitiesResult { Id = other, Code = CloudEntityCodes.CloudFolder },
            new GetEntitiesResult { Id = main, Code = CloudEntityCodes.CloudFolder }
        };
        var picked = CloudMainParent.PickParent(main, parents);
        Assert.NotNull(picked);
        Assert.Equal(main, picked!.Id);
    }

    [Fact]
    public void PickParent_WithoutMain_PrefersCloudFolder()
    {
        var folder = Guid.NewGuid();
        var parents = new[]
        {
            new GetEntitiesResult { Id = Guid.NewGuid(), Code = "User" },
            new GetEntitiesResult { Id = folder, Code = CloudEntityCodes.CloudFolder }
        };
        var picked = CloudMainParent.PickParent(null, parents);
        Assert.Equal(folder, picked!.Id);
    }

    [Fact]
    public void PickParent_Empty_ReturnsNull()
    {
        Assert.Null(CloudMainParent.PickParent(Guid.NewGuid(), Array.Empty<GetEntitiesResult>()));
    }
}
