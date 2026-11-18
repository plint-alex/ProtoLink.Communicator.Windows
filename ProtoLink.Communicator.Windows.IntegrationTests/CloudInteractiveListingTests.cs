using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

public class CloudInteractiveListingTests
{
    [Fact]
    public void ResolveRowName_PrefersNameTypeValue_OverSyncMeta()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var entity = new GetEntitiesResult
        {
            Id = id,
            Code = CloudEntityCodes.CloudFolder,
            Values =
            [
                new EntityValue
                {
                    Value = "FromValues",
                    Parents = [CloudEntityCodes.NameTypeId]
                }
            ]
        };

        var name = CloudInteractiveListing.ResolveRowName(entity, _ => "FromMeta");
        Assert.Equal("FromValues", name);
    }

    [Fact]
    public void ResolveRowName_UsesSyncMeta_WhenValuesMissing()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var entity = new GetEntitiesResult
        {
            Id = id,
            Code = CloudEntityCodes.CloudFolder
        };

        var name = CloudInteractiveListing.ResolveRowName(
            entity,
            rid => rid == id ? "agentteamverify" : null);
        Assert.Equal("agentteamverify", name);
    }

    [Fact]
    public void ResolveRowName_FallsBackToSynthetic_WhenNoValuesOrMeta()
    {
        var id = Guid.Parse("99999999-8888-7777-6666-555555555555");
        var entity = new GetEntitiesResult
        {
            Id = id,
            Code = CloudEntityCodes.CloudFolder
        };

        var name = CloudInteractiveListing.ResolveRowName(entity, _ => null);
        Assert.Equal($"folder-{id:N}", name);
        Assert.True(CloudInteractiveListing.IsSyntheticCloudDisplayName(entity, name));
    }

    [Fact]
    public void LeafName_KeepsCase_AndTakesLastSegment()
    {
        Assert.Equal("AgentTeamVerify", CloudInteractiveListing.LeafName(@"Notes\AgentTeamVerify"));
        Assert.Equal("file.txt", CloudInteractiveListing.LeafName("a/b/file.txt"));
        Assert.Equal("", CloudInteractiveListing.LeafName(""));
    }

    [Fact]
    public void InteractiveListing_UsesSinglePage_AndDocumentsValuesLightPairing()
    {
        Assert.Equal(1, CloudApiService.InteractiveListingMaxPages);
        Assert.Equal(200, CloudApiService.GetEntitiesPageSize);
    }
}
