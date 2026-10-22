using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class LocalPathCoverageTests
{
    [Fact]
    public void NotesRoot_Equal_to_mapping_is_covered()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-map-eq-" + Guid.NewGuid().ToString("N"));
        Assert.True(LocalPathCoverage.IsCoveredByAnyMappingRoot(root, new[] { root }));
    }

    [Fact]
    public void NotesRoot_under_mapping_is_covered()
    {
        var mapping = Path.Combine(Path.GetTempPath(), "pl-map-outer-" + Guid.NewGuid().ToString("N"));
        var notes = Path.Combine(mapping, "MyNotesFolder");
        Assert.True(LocalPathCoverage.IsCoveredByAnyMappingRoot(notes, new[] { mapping }));
    }

    [Fact]
    public void NotesRoot_sibling_of_mapping_is_not_covered()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "pl-map-sib-" + Guid.NewGuid().ToString("N"));
        var mapping = Path.Combine(baseDir, "CloudMapped");
        var notes = Path.Combine(baseDir, "MyNotesFolder");
        Assert.False(LocalPathCoverage.IsCoveredByAnyMappingRoot(notes, new[] { mapping }));
    }

    [Fact]
    public void NotesRoot_prefix_false_friend_not_covered()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "pl-map-pfx-" + Guid.NewGuid().ToString("N"));
        var mapping = Path.Combine(baseDir, "Notes");
        var notes = Path.Combine(baseDir, "NotesBackup");
        Assert.False(LocalPathCoverage.IsCoveredByAnyMappingRoot(notes, new[] { mapping }));
    }

    [Fact]
    public void Empty_or_invalid_notes_root_not_covered()
    {
        Assert.False(LocalPathCoverage.IsCoveredByAnyMappingRoot(null, new[] { @"C:\Maps" }));
        Assert.False(LocalPathCoverage.IsCoveredByAnyMappingRoot("  ", new[] { @"C:\Maps" }));
        Assert.False(LocalPathCoverage.IsCoveredByAnyMappingRoot(@"C:\Notes", Array.Empty<string?>()));
    }

    [Fact]
    public void Mapping_root_under_notes_does_not_cover_notes_root()
    {
        var notes = Path.Combine(Path.GetTempPath(), "pl-map-notes-" + Guid.NewGuid().ToString("N"));
        var nestedMap = Path.Combine(notes, "sub");
        Assert.False(LocalPathCoverage.IsCoveredByAnyMappingRoot(notes, new[] { nestedMap }));
    }
}
