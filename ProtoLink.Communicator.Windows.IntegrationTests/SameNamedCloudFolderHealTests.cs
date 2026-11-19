using ProtoLink.Communicator.Windows.Services.Sync;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// FAIL class P1-20261117 / D-HEAL-WIN: Android mapped duplicate MyNotesFolder 0bc549dd
/// while Win/AC use populated 898c1453. Prefer-populated + cross-CloudRoot discover.
/// </summary>
[Trait("Category", "Offline")]
public sealed class SameNamedCloudFolderHealTests
{
    private static readonly Guid WinId = Guid.Parse("898c1453-4034-4b9f-96f6-430b4ceca476");
    private static readonly Guid DupId = Guid.Parse("0bc549dd-c880-42df-af32-4f3c340ba970");
    private const string Local = @"D:\tmp\MyNotesFolder";

    private static readonly SameNamedCloudFolderHeal.Sibling[] SiblingsWinOldest =
    {
        new(DupId, "MyNotesFolder", "2026-11-15T10:00:00", 0),
        new(WinId, "MyNotesFolder", "2026-10-29T18:49:32.7901832", 0),
        new(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "Other", "2026-01-01T00:00:00", 0)
    };

    private static readonly SameNamedCloudFolderHeal.Sibling[] SiblingsLiveEmptyOlder =
    {
        new(DupId, "MyNotesFolder", "2026-10-28T12:00:00", 0),
        new(WinId, "MyNotesFolder", "2026-10-29T18:49:32.7901832", 12),
        new(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "Other", "2026-01-01T00:00:00", 0)
    };

    [Fact]
    public void PreferExistingSameName_PicksOldestWhenChildCountsEqual()
    {
        var pick = SameNamedCloudFolderHeal.PreferExistingSameName("MyNotesFolder", SiblingsWinOldest);
        Assert.NotNull(pick);
        Assert.Equal(WinId, pick!.Value.Id);
    }

    [Fact]
    public void PreferExistingSameName_PrefersPopulatedOverOlderEmpty()
    {
        var pick = SameNamedCloudFolderHeal.PreferExistingSameName("MyNotesFolder", SiblingsLiveEmptyOlder);
        Assert.NotNull(pick);
        Assert.Equal(WinId, pick!.Value.Id);
    }

    [Fact]
    public void PreferExistingSameName_NoMatch_ReturnsNull()
    {
        Assert.Null(SameNamedCloudFolderHeal.PreferExistingSameName("BrandNew", SiblingsWinOldest));
    }

    [Fact]
    public void CanonicalIdIfMappedIsDuplicate_RemapsNewerDupToWin()
    {
        Assert.Equal(
            WinId,
            SameNamedCloudFolderHeal.CanonicalIdIfMappedIsDuplicate(DupId, "MyNotesFolder", SiblingsWinOldest));
    }

    [Fact]
    public void CanonicalIdIfMappedIsDuplicate_RemapsOlderEmptyDupToPopulatedWin()
    {
        Assert.Equal(
            WinId,
            SameNamedCloudFolderHeal.CanonicalIdIfMappedIsDuplicate(DupId, "MyNotesFolder", SiblingsLiveEmptyOlder));
    }

    [Fact]
    public void CanonicalIdIfMappedIsDuplicate_AlreadyCanonical_Null()
    {
        Assert.Null(
            SameNamedCloudFolderHeal.CanonicalIdIfMappedIsDuplicate(WinId, "MyNotesFolder", SiblingsLiveEmptyOlder));
    }

    /// <summary>
    /// Demotion FAIL class (Win 1.0.94): raw GetEntities Count preferred ghost-file twin over
    /// folder-rich 898. After FolderChildCount enrich, ImmediateChildCount is folders-only —
    /// twin junk files must not demote a mapped content-bearing 898.
    /// </summary>
    [Fact]
    public void PreferExisting_FolderRichCanonicalBeatsJunkFileTwin()
    {
        var siblings = new[]
        {
            new SameNamedCloudFolderHeal.Sibling(DupId, "MyNotesFolder", "2026-10-28T09:46:55", 0),
            new SameNamedCloudFolderHeal.Sibling(WinId, "MyNotesFolder", "2026-10-29T18:49:32", 24)
        };
        var pick = SameNamedCloudFolderHeal.PreferExistingSameName("MyNotesFolder", siblings);
        Assert.NotNull(pick);
        Assert.Equal(WinId, pick!.Value.Id);
        Assert.Null(
            SameNamedCloudFolderHeal.CanonicalIdIfMappedIsDuplicate(WinId, "MyNotesFolder", siblings));
    }

    [Fact]
    public void Remaps_MappedCanonical_NotDemotedWhenTwinHasZeroFolders()
    {
        var parent = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var siblings = new[]
        {
            new SameNamedCloudFolderHeal.Sibling(DupId, "MyNotesFolder", "2026-10-28T09:46:55", 0),
            new SameNamedCloudFolderHeal.Sibling(WinId, "MyNotesFolder", "2026-10-29T18:49:32", 24)
        };
        var remaps = SameNamedCloudFolderHeal.RemapsForDuplicateNames(
            new[] { new SameNamedCloudFolderHeal.MappingRow(WinId, Local, "MyNotesFolder") },
            new Dictionary<Guid, IReadOnlyList<SameNamedCloudFolderHeal.Sibling>>
            {
                [parent] = siblings
            },
            new Dictionary<Guid, Guid> { [WinId] = parent });
        Assert.Empty(remaps);
    }

    [Fact]
    public void Remaps_TwinJunk_HealsToFolderRichCanonical()
    {
        var parent = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var siblings = new[]
        {
            new SameNamedCloudFolderHeal.Sibling(DupId, "MyNotesFolder", "2026-10-28T09:46:55", 0),
            new SameNamedCloudFolderHeal.Sibling(WinId, "MyNotesFolder", "2026-10-29T18:49:32", 24)
        };
        var remaps = SameNamedCloudFolderHeal.RemapsForDuplicateNames(
            new[] { new SameNamedCloudFolderHeal.MappingRow(DupId, Local, "MyNotesFolder") },
            new Dictionary<Guid, IReadOnlyList<SameNamedCloudFolderHeal.Sibling>>
            {
                [parent] = siblings
            },
            new Dictionary<Guid, Guid> { [DupId] = parent });
        Assert.Single(remaps);
        Assert.Equal(DupId, remaps[0].FromId);
        Assert.Equal(WinId, remaps[0].ToId);
    }

    /// <summary>
    /// FAIL after FolderChildCount tip: near-clone twin (29 folders + 86 junk files) still
    /// outranked 898 (28+25) on folders alone — HealRichnessScore must prefer 898.
    /// </summary>
    [Fact]
    public void PreferExisting_NearCloneJunkTwinLosesToCanonical898()
    {
        var siblings = new[]
        {
            new SameNamedCloudFolderHeal.Sibling(
                DupId, "MyNotesFolder", "2026-10-28T09:46:55",
                ImmediateChildCount: 29, ImmediateFileCount: 86),
            new SameNamedCloudFolderHeal.Sibling(
                WinId, "MyNotesFolder", "2026-10-29T18:49:32",
                ImmediateChildCount: 28, ImmediateFileCount: 25)
        };
        var pick = SameNamedCloudFolderHeal.PreferExistingSameName("MyNotesFolder", siblings);
        Assert.NotNull(pick);
        Assert.Equal(WinId, pick!.Value.Id);
        Assert.Equal(WinId,
            SameNamedCloudFolderHeal.CanonicalIdIfMappedIsDuplicate(DupId, "MyNotesFolder", siblings));
        Assert.Null(
            SameNamedCloudFolderHeal.CanonicalIdIfMappedIsDuplicate(WinId, "MyNotesFolder", siblings));
    }

    [Fact]
    public void RemapsAndApply_HealsDupMappingWhenEmptyIsOlder()
    {
        var mappings = new[]
        {
            new SameNamedCloudFolderHeal.MappingRow(DupId, Local, "MyNotesFolder")
        };
        var parent = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var remaps = SameNamedCloudFolderHeal.RemapsForDuplicateNames(
            mappings,
            new Dictionary<Guid, IReadOnlyList<SameNamedCloudFolderHeal.Sibling>>
            {
                [parent] = SiblingsLiveEmptyOlder
            },
            new Dictionary<Guid, Guid> { [DupId] = parent });
        Assert.Single(remaps);
        Assert.Equal(DupId, remaps[0].FromId);
        Assert.Equal(WinId, remaps[0].ToId);

        var healed = SameNamedCloudFolderHeal.ApplyRemaps(mappings, remaps);
        Assert.Single(healed);
        Assert.Equal(WinId, healed[0].CloudFolderId);
        Assert.Equal(Local, healed[0].LocalPath);
    }

    [Fact]
    public void DisplayLabel_ShowsShortIdWhenDuplicateNames()
    {
        var names = new[] { "MyNotesFolder", "MyNotesFolder", "Other" };
        Assert.Equal(
            "MyNotesFolder · 898c1453",
            SameNamedCloudFolderHeal.DisplayLabel("MyNotesFolder", WinId, names));
        Assert.Equal(
            "Other",
            SameNamedCloudFolderHeal.DisplayLabel(
                "Other",
                Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),
                names));
    }

    [Fact]
    public void MergeById_DedupesAndKeepsHigherChildCount()
    {
        var a = new SameNamedCloudFolderHeal.Sibling(DupId, "MyNotesFolder", "2026-10-28T12:00:00", 0);
        var b = new SameNamedCloudFolderHeal.Sibling(DupId, "MyNotesFolder", null, 75);
        var c = new SameNamedCloudFolderHeal.Sibling(WinId, "MyNotesFolder", "2026-10-29T18:49:32", 306);
        var merged = SameNamedCloudFolderHeal.MergeById(new[] { a }, new[] { b, c });
        Assert.Equal(2, merged.Count);
        Assert.Equal(75, merged.First(s => s.Id == DupId).ImmediateChildCount);
        Assert.Equal(306, merged.First(s => s.Id == WinId).ImmediateChildCount);
    }

    [Fact]
    public void CloudRootIdsForDiscovery_IncludesMustDriveAndOldest()
    {
        var androidRoot = Guid.Parse("8aec679c-a389-46b9-8105-8a5b238ee979");
        var driveRoot = Guid.Parse("3e5a2d6e-27ca-4c8b-89e6-e3cb253c05d6");
        var otherNew = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var roots = new[]
        {
            new SameNamedCloudFolderHeal.Sibling(otherNew, "Scratch", "2026-11-17T00:00:00"),
            new SameNamedCloudFolderHeal.Sibling(androidRoot, "", "2026-10-28T09:00:00"),
            new SameNamedCloudFolderHeal.Sibling(driveRoot, "Drive", "2026-02-28T16:35:14")
        };
        var picked = SameNamedCloudFolderHeal.CloudRootIdsForDiscovery(
            roots,
            new[] { androidRoot },
            maxRoots: 16);
        Assert.Contains(androidRoot, picked);
        Assert.Contains(driveRoot, picked);
    }

    [Fact]
    public void Remaps_CrossCloudRootMergedSiblings_PrefersPopulatedWin()
    {
        var androidRoot = Guid.Parse("8aec679c-a389-46b9-8105-8a5b238ee979");
        var underAndroid = new[]
        {
            new SameNamedCloudFolderHeal.Sibling(DupId, "MyNotesFolder", "2026-10-28T09:46:55", 75)
        };
        var underDrive = new[]
        {
            new SameNamedCloudFolderHeal.Sibling(WinId, "MyNotesFolder", "2026-10-29T18:49:32", 306)
        };
        var merged = SameNamedCloudFolderHeal.MergeById(underAndroid, underDrive);
        var remaps = SameNamedCloudFolderHeal.RemapsForDuplicateNames(
            new[] { new SameNamedCloudFolderHeal.MappingRow(DupId, Local, "MyNotesFolder") },
            new Dictionary<Guid, IReadOnlyList<SameNamedCloudFolderHeal.Sibling>>
            {
                [androidRoot] = merged
            },
            new Dictionary<Guid, Guid> { [DupId] = androidRoot });
        Assert.Single(remaps);
        Assert.Equal(DupId, remaps[0].FromId);
        Assert.Equal(WinId, remaps[0].ToId);
    }
}
