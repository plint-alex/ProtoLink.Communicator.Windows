using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Sync;
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
        Assert.Equal(80, CloudApiService.InteractiveListingPageSize);
        Assert.True(CloudApiService.InteractiveListingPageSize < CloudApiService.GetEntitiesPageSize);
    }

    [Fact]
    public void BuildSoftReadyRows_TakesDirectChildren_OrderedFoldersFirst()
    {
        var parent = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var folderId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var fileId = Guid.Parse("99999999-8888-7777-6666-555555555555");
        var rows = CloudInteractiveListing.BuildSoftReadyRows(
            parent,
            new[]
            {
                new SyncItemMeta
                {
                    RemoteId = fileId,
                    ParentRemoteId = parent,
                    RelativePath = "Notes/zzz.txt",
                    IsFolder = false,
                    RemoteUpdateTime = DateTime.UtcNow.AddMinutes(-1)
                },
                new SyncItemMeta
                {
                    RemoteId = folderId,
                    ParentRemoteId = parent,
                    RelativePath = "Notes/agentteamverify",
                    IsFolder = true,
                    RemoteUpdateTime = DateTime.UtcNow
                },
                new SyncItemMeta
                {
                    RemoteId = Guid.NewGuid(),
                    ParentRemoteId = Guid.NewGuid(),
                    RelativePath = "Notes/other/skip",
                    IsFolder = true
                }
            },
            maxRows: 10);

        Assert.Equal(2, rows.Count);
        Assert.Equal(folderId, rows[0].Id);
        Assert.Equal("agentteamverify", rows[0].Name);
        Assert.True(rows[0].IsFolder);
        Assert.Equal(fileId, rows[1].Id);
        Assert.Equal("zzz.txt", rows[1].Name);
    }
}
