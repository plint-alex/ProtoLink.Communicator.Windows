using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class MapLocalFolderWizardCandidatesTests
{
    private static GetEntitiesResult Folder(Guid id, string name) => new()
    {
        Id = id,
        Code = CloudEntityCodes.CloudFolder,
        TypeEntityId = CloudEntityCodes.FolderKindEntityId,
        Values = new List<EntityValue>
        {
            new() { Value = name, Parents = [CloudEntityCodes.NameTypeId] }
        }
    };

    [Fact]
    public void FromParentListing_SkipsMappedAndCloudRoot()
    {
        var mappedId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var keepId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var rootId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var entities = new[]
        {
            Folder(mappedId, "Mapped"),
            Folder(keepId, "Keep"),
            new GetEntitiesResult
            {
                Id = rootId,
                Code = CloudEntityCodes.CloudRoot,
                TypeEntityId = CloudEntityCodes.FolderKindEntityId,
                Values = new List<EntityValue>
                {
                    new() { Value = "Root", Parents = [CloudEntityCodes.NameTypeId] }
                }
            }
        };

        var result = MapLocalFolderWizardCandidates.FromParentListing(
            entities,
            new HashSet<Guid> { mappedId });

        Assert.Single(result);
        Assert.Equal(keepId, result[0].Id);
        Assert.Equal("Keep", result[0].Name);
    }

    [Fact]
    public void FromParentListing_DisambiguatesDuplicateNames()
    {
        var a = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var b = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var entities = new[]
        {
            Folder(a, "Twin"),
            Folder(b, "Twin")
        };

        var result = MapLocalFolderWizardCandidates.FromParentListing(
            entities,
            new HashSet<Guid>());

        Assert.Equal(2, result.Count);
        Assert.All(result, c => Assert.Contains("Twin", c.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result, c => c.Name.Contains('·'));
    }

    [Fact]
    public void PreferSameNamedUnderParent_FindsCaseInsensitiveMatch()
    {
        var id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var hit = MapLocalFolderWizardCandidates.PreferSameNamedUnderParent(
            "MyNotesFolder",
            new[] { Folder(id, "mynotesfolder") });

        Assert.NotNull(hit);
        Assert.Equal(id, hit!.Value.Id);
    }

    [Fact]
    public void PreferSameNamedUnderParent_ReturnsNullWhenMissing()
    {
        var hit = MapLocalFolderWizardCandidates.PreferSameNamedUnderParent(
            "Missing",
            new[] { Folder(Guid.NewGuid(), "Other") });
        Assert.Null(hit);
    }
}
