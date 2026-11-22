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
    public void ResolveAlternateFolderName_PrefersMeta_ThenMapping_ThenUi()
    {
        var id = Guid.Parse("321e6beb-1c55-48ca-9a02-5b4405212959");
        Assert.Equal(
            "FromMeta",
            CloudInteractiveListing.ResolveAlternateFolderName(id, "FromMeta", "User_Test_1", "UiName"));
        Assert.Equal(
            "User_Test_1",
            CloudInteractiveListing.ResolveAlternateFolderName(id, null, "User_Test_1", "UiName"));
        Assert.Equal(
            "UiName",
            CloudInteractiveListing.ResolveAlternateFolderName(id, "  ", null, "UiName"));
        Assert.Null(
            CloudInteractiveListing.ResolveAlternateFolderName(
                id, null, null, $"folder-{id:N}"));
    }

    [Fact]
    public void ResolveRowName_UsesMappingName_ViaAlternateResolver()
    {
        var id = Guid.Parse("321e6beb-1c55-48ca-9a02-5b4405212959");
        var entity = new GetEntitiesResult
        {
            Id = id,
            Code = CloudEntityCodes.CloudFolder
        };

        var name = CloudInteractiveListing.ResolveRowName(
            entity,
            rid => CloudInteractiveListing.ResolveAlternateFolderName(
                rid, syncMetaLeafName: null, mappingCloudFolderName: "User_Test_1"));
        Assert.Equal("User_Test_1", name);
        Assert.False(CloudInteractiveListing.IsSyntheticCloudDisplayName(entity, name));
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
    public void ShouldReassertDisplayName_RealFolderName_NotSynthetic()
    {
        var id = Guid.Parse("12984c41-556d-46ed-84e3-0a524d58c899");
        Assert.True(CloudApiService.ShouldReassertDisplayName(id, "User_Test_1", isFolder: true));
        Assert.False(CloudApiService.ShouldReassertDisplayName(id, $"folder-{id:N}", isFolder: true));
        Assert.False(CloudApiService.ShouldReassertDisplayName(id, "  ", isFolder: true));
        Assert.False(CloudApiService.ShouldReassertDisplayName(id, null, isFolder: true));
    }

    [Fact]
    public void LeafName_KeepsCase_AndTakesLastSegment()
    {
        Assert.Equal("AgentTeamVerify", CloudInteractiveListing.LeafName(@"Notes\AgentTeamVerify"));
        Assert.Equal("file.txt", CloudInteractiveListing.LeafName("a/b/file.txt"));
        Assert.Equal("", CloudInteractiveListing.LeafName(""));
    }

    [Fact]
    public void FolderChildCount_IgnoresCloudFiles()
    {
        var kids = new[]
        {
            new GetEntitiesResult { Id = Guid.NewGuid(), Code = CloudEntityCodes.CloudFolder },
            new GetEntitiesResult { Id = Guid.NewGuid(), Code = CloudEntityCodes.CloudFolder },
            new GetEntitiesResult { Id = Guid.NewGuid(), Code = CloudEntityCodes.CloudFile },
            new GetEntitiesResult { Id = Guid.NewGuid(), Code = CloudEntityCodes.CloudFile }
        };
        Assert.Equal(2, CloudInteractiveListing.FolderChildCount(kids));
    }

    [Fact]
    public void FolderChildCount_PrefersFolderRichCanonicalOverJunkTwin()
    {
        // Live FAIL: twin 0bc549dd ~132 files vs 898c1453 ~24 folders — raw Count wrongly picks twin.
        var twinKids = Enumerable.Range(0, 132)
            .Select(_ => new GetEntitiesResult { Id = Guid.NewGuid(), Code = CloudEntityCodes.CloudFile })
            .ToArray();
        var canonicalKids = Enumerable.Range(0, 24)
            .Select(_ => new GetEntitiesResult { Id = Guid.NewGuid(), Code = CloudEntityCodes.CloudFolder })
            .Concat(Enumerable.Range(0, 21)
                .Select(_ => new GetEntitiesResult { Id = Guid.NewGuid(), Code = CloudEntityCodes.CloudFile }))
            .ToArray();
        Assert.Equal(0, CloudInteractiveListing.FolderChildCount(twinKids));
        Assert.Equal(24, CloudInteractiveListing.FolderChildCount(canonicalKids));
        Assert.True(
            CloudInteractiveListing.FolderChildCount(canonicalKids)
            > CloudInteractiveListing.FolderChildCount(twinKids));
    }

    [Fact]
    public void InteractiveListing_PagesCoverMyNotesFolderScale_ValuesLightPairing()
    {
        // FAIL CLOUD_LISTING_DIVERGE_FOLDER_SET: MaxPages=1 dropped structural folders past 80.
        Assert.Equal(5, CloudApiService.InteractiveListingMaxPages);
        Assert.Equal(200, CloudApiService.GetEntitiesPageSize);
        Assert.Equal(80, CloudApiService.InteractiveListingPageSize);
        Assert.Equal(400, CloudApiService.InteractiveListingMaxRows);
        Assert.True(CloudApiService.InteractiveListingMaxRows >= 115);
        Assert.True(CloudApiService.InteractiveListingPageSize < CloudApiService.GetEntitiesPageSize);
    }

    [Fact]
    public void HealRichnessScore_NearCloneJunkTwinLosesToCanonical()
    {
        var twin = CloudInteractiveListing.HealRichnessScore(29, 86);
        var canonical = CloudInteractiveListing.HealRichnessScore(28, 25);
        Assert.True(canonical > twin);
        Assert.True(
            CloudInteractiveListing.HealRichnessScore(24, 0)
            > CloudInteractiveListing.HealRichnessScore(0, 132));
    }

    [Fact]
    public void MultiPageFetch_ThenFoldersFirst_KeepsStructuralFoldersPastFirst80()
    {
        var files = Enumerable.Range(1, 86)
            .Select(i => new GetEntitiesResult
            {
                Id = Guid.NewGuid(),
                Code = CloudEntityCodes.CloudFile,
                UpdateTime = DateTime.UtcNow.AddMinutes(-i),
                Values =
                [
                    new EntityValue { Value = $"junk-{i}.txt", Parents = [CloudEntityCodes.NameTypeId] }
                ]
            })
            .ToList();
        var folderNames = new[] { "dev ops", "docusign", "ef commands", "films" };
        var folders = folderNames.Select((n, i) => new GetEntitiesResult
            {
                Id = Guid.NewGuid(),
                Code = CloudEntityCodes.CloudFolder,
                UpdateTime = DateTime.UtcNow.AddDays(-2).AddMinutes(i),
                Values =
                [
                    new EntityValue { Value = n, Parents = [CloudEntityCodes.NameTypeId] }
                ]
            })
            .ToList();
        var apiOrder = files.Concat(folders).ToList();
        var page1 = apiOrder.Take(CloudApiService.InteractiveListingPageSize).ToList();
        var page2 = apiOrder.Skip(CloudApiService.InteractiveListingPageSize)
            .Take(CloudApiService.InteractiveListingPageSize).ToList();
        Assert.DoesNotContain(page1, e => e.Code == CloudEntityCodes.CloudFolder);
        var fetched = page1.Concat(page2).ToList();
        var painted = fetched
            .Select(e => (
                Name: CloudInteractiveListing.ResolveRowName(e),
                IsFolder: e.Code == CloudEntityCodes.CloudFolder,
                Update: e.UpdateTime))
            .OrderByDescending(x => x.IsFolder)
            .ThenByDescending(x => x.Update)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.Contains(painted, x => x.Name == "dev ops");
        Assert.Contains(painted, x => x.Name == "docusign");
        Assert.Contains(painted, x => x.Name == "ef commands");
        Assert.Equal(4, painted.Count(x => x.IsFolder));
        Assert.DoesNotContain(
            page1.Select(e => CloudInteractiveListing.ResolveRowName(e)),
            n => n == "dev ops");
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
