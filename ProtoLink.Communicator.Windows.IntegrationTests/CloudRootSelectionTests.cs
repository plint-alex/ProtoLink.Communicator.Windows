using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class CloudRootSelectionTests
{
    [Fact]
    public void PickCanonical_SingleRoot_ReturnsIt()
    {
        var id = Guid.Parse("8aec679c-a389-46b9-8105-8a5b238ee979");
        var roots = new[] { new CloudRootSelection.Candidate(id, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)) };
        Assert.Equal(id, CloudRootSelection.PickCanonical(roots));
    }

    [Fact]
    public void PickCanonical_PrefersNonMappedWhenDuplicate()
    {
        var healthy = Guid.Parse("8aec679c-a389-46b9-8105-8a5b238ee979");
        var drive = Guid.Parse("3e5a2d6e-27ca-4c8b-89e6-e3cb253c05d6");
        var roots = new[]
        {
            new CloudRootSelection.Candidate(drive, new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new CloudRootSelection.Candidate(healthy, new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc))
        };
        var mapped = new HashSet<Guid> { drive };
        Assert.Equal(healthy, CloudRootSelection.PickCanonical(roots, mapped));
    }

    [Fact]
    public void PickCanonical_WhenAllMapped_PrefersEarliestCreation()
    {
        var older = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var newer = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var roots = new[]
        {
            new CloudRootSelection.Candidate(newer, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new CloudRootSelection.Candidate(older, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        };
        var mapped = new HashSet<Guid> { older, newer };
        Assert.Equal(older, CloudRootSelection.PickCanonical(roots, mapped));
    }

    [Fact]
    public void PickCanonical_KeepsPreferredWhenInNonMappedPool()
    {
        var a = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var b = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var roots = new[]
        {
            new CloudRootSelection.Candidate(a, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new CloudRootSelection.Candidate(b, new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        };
        Assert.Equal(b, CloudRootSelection.PickCanonical(roots, preferredId: b));
    }

    /// <summary>
    /// Agents FAIL 1.0.56: mappings [] → both roots non-mapped → earliest CreationTime crowned Drive;
    /// LastOpen healthy must win via preferredId so ShouldSkipLastOpenCloudRoot does not drop it.
    /// </summary>
    [Fact]
    public void PickCanonical_WhenMappingsEmpty_PrefersLastOpenOverEarliestDrive()
    {
        var healthy = Guid.Parse("8aec679c-a389-46b9-8105-8a5b238ee979");
        var drive = Guid.Parse("3e5a2d6e-27ca-4c8b-89e6-e3cb253c05d6");
        var roots = new[]
        {
            new CloudRootSelection.Candidate(drive, new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new CloudRootSelection.Candidate(healthy, new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc))
        };
        var mappedEmpty = new HashSet<Guid>();
        Assert.Equal(drive, CloudRootSelection.PickCanonical(roots, mappedEmpty));
        Assert.Equal(healthy, CloudRootSelection.PickCanonical(roots, mappedEmpty, preferredId: healthy));
        Assert.False(CloudRootSelection.ShouldSkipLastOpenCloudRoot(
            CloudEntityCodes.CloudRoot, healthy, healthy, cloudRootCountUnderUser: 2));
    }

    [Fact]
    public void ResolvePreferredCloudRootId_PrefersLastOpenCloudRootOverStaleMyCloud()
    {
        var healthy = Guid.Parse("8aec679c-a389-46b9-8105-8a5b238ee979");
        var drive = Guid.Parse("3e5a2d6e-27ca-4c8b-89e6-e3cb253c05d6");
        var known = new HashSet<Guid> { healthy, drive };
        Assert.Equal(healthy, CloudRootSelection.ResolvePreferredCloudRootId(known, healthy, drive));
        Assert.Equal(drive, CloudRootSelection.ResolvePreferredCloudRootId(known, Guid.NewGuid(), drive));
        Assert.Null(CloudRootSelection.ResolvePreferredCloudRootId(known, Guid.NewGuid(), Guid.Empty));
    }

    [Fact]
    public void PickWizardCreateParentRoot_PrefersNewestNonMapped_NotOldestDrive()
    {
        var healthy = Guid.Parse("8aec679c-a389-46b9-8105-8a5b238ee979");
        var drive = Guid.Parse("3e5a2d6e-27ca-4c8b-89e6-e3cb253c05d6");
        var roots = new[]
        {
            new CloudRootSelection.Candidate(drive, new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new CloudRootSelection.Candidate(healthy, new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc))
        };
        Assert.Equal(healthy, CloudRootSelection.PickWizardCreateParentRoot(roots));
        Assert.Equal(healthy, CloudRootSelection.PickWizardCreateParentRoot(roots, new HashSet<Guid>()));
    }

    [Fact]
    public void ShouldRefuseMapping_CloudRootCode()
    {
        Assert.True(CloudRootSelection.ShouldRefuseMapping(CloudEntityCodes.CloudRoot, Guid.NewGuid()));
        Assert.False(CloudRootSelection.ShouldRefuseMapping(CloudEntityCodes.CloudFolder, Guid.NewGuid()));
    }

    [Fact]
    public void ShouldRefuseMapping_KnownCloudRootIdWithoutCode()
    {
        var drive = Guid.Parse("3e5a2d6e-27ca-4c8b-89e6-e3cb253c05d6");
        var known = new HashSet<Guid> { drive };
        Assert.True(CloudRootSelection.ShouldRefuseMapping(null, drive, known));
        Assert.False(CloudRootSelection.ShouldRefuseMapping(
            CloudEntityCodes.CloudFolder, Guid.NewGuid(), known));
    }

    [Fact]
    public void ShouldSkipLastOpenCloudRoot_OnlyNonCanonicalWhenDuplicates()
    {
        var healthy = Guid.Parse("8aec679c-a389-46b9-8105-8a5b238ee979");
        var drive = Guid.Parse("3e5a2d6e-27ca-4c8b-89e6-e3cb253c05d6");
        Assert.True(CloudRootSelection.ShouldSkipLastOpenCloudRoot(
            CloudEntityCodes.CloudRoot, drive, healthy, cloudRootCountUnderUser: 2));
        Assert.False(CloudRootSelection.ShouldSkipLastOpenCloudRoot(
            CloudEntityCodes.CloudRoot, healthy, healthy, cloudRootCountUnderUser: 2));
        Assert.False(CloudRootSelection.ShouldSkipLastOpenCloudRoot(
            CloudEntityCodes.CloudRoot, drive, healthy, cloudRootCountUnderUser: 1));
        Assert.False(CloudRootSelection.ShouldSkipLastOpenCloudRoot(
            CloudEntityCodes.CloudFolder, drive, healthy, cloudRootCountUnderUser: 2));
    }

    [Fact]
    public void ShouldFallbackToHubAfterListingFailure_OnKnownCloudRoot()
    {
        var drive = Guid.Parse("3e5a2d6e-27ca-4c8b-89e6-e3cb253c05d6");
        var known = new HashSet<Guid> { drive };
        Assert.True(CloudRootSelection.ShouldFallbackToHubAfterListingFailure(drive, known));
        Assert.False(CloudRootSelection.ShouldFallbackToHubAfterListingFailure(Guid.NewGuid(), known));
    }

    [Fact]
    public void IsNotesRootPath_NormalizesTrailingSeparators()
    {
        var root = Path.Combine(Path.GetTempPath(), "ProtoLinkNotesRootTest");
        Directory.CreateDirectory(root);
        try
        {
            Assert.True(CloudRootSelection.IsNotesRootPath(root + Path.DirectorySeparatorChar, root));
            Assert.False(CloudRootSelection.IsNotesRootPath(Path.Combine(root, "child"), root));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }
}
