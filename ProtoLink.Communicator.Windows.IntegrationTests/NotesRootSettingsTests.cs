using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class NotesRootSettingsTests
{
    [Fact]
    public void ResolveRoots_LegacySinglePath_BecomesOneItemList()
    {
        var settings = new AppSettings { NotesRootPath = @"C:\Notes\Work" };
        var roots = NotesRootSettings.ResolveRoots(settings);
        Assert.Single(roots);
        Assert.True(NotesRootSettings.PathsEqual(roots[0], @"C:\Notes\Work"));
    }

    [Fact]
    public void ResolveRoots_PrefersNotesRootPaths_OverLegacy()
    {
        var settings = new AppSettings
        {
            NotesRootPath = @"C:\Legacy",
            NotesRootPaths = new List<string> { @"C:\A", @"C:\B" }
        };
        var roots = NotesRootSettings.ResolveRoots(settings);
        Assert.Equal(2, roots.Count);
        Assert.True(NotesRootSettings.PathsEqual(roots[0], @"C:\A"));
        Assert.True(NotesRootSettings.PathsEqual(roots[1], @"C:\B"));
    }

    [Fact]
    public void ResolveRoots_DedupesNormalizedPaths()
    {
        var settings = new AppSettings
        {
            NotesRootPaths = new List<string>
            {
                @"C:\Notes\Work\",
                @"C:\Notes\Work",
                @"C:\Notes\Personal"
            }
        };
        var roots = NotesRootSettings.ResolveRoots(settings);
        Assert.Equal(2, roots.Count);
    }

    [Fact]
    public void ResolveSelected_UsesLastSelected_WhenInList()
    {
        var settings = new AppSettings
        {
            NotesRootPaths = new List<string> { @"C:\A", @"C:\B" },
            LastSelectedNotesRootPath = @"C:\B\",
            NotesRootPath = @"C:\A"
        };
        var selected = NotesRootSettings.ResolveSelected(settings);
        Assert.True(NotesRootSettings.PathsEqual(selected, @"C:\B"));
    }

    [Fact]
    public void Write_MirrorsSelectedToLegacyNotesRootPath()
    {
        var settings = new AppSettings();
        NotesRootSettings.Write(settings, new[] { @"C:\A", @"C:\B" }, @"C:\B");
        Assert.Equal(2, settings.NotesRootPaths.Count);
        Assert.True(NotesRootSettings.PathsEqual(settings.LastSelectedNotesRootPath, @"C:\B"));
        Assert.True(NotesRootSettings.PathsEqual(settings.NotesRootPath, @"C:\B"));
    }

    [Fact]
    public void PreferNeighborAfterRemove_PrefersPrevious()
    {
        var roots = new List<string> { @"C:\A", @"C:\B", @"C:\C" };
        Assert.Equal(@"C:\A", NotesRootSettings.PreferNeighborAfterRemove(roots, 1));
        Assert.Equal(@"C:\B", NotesRootSettings.PreferNeighborAfterRemove(roots, 0));
        Assert.Null(NotesRootSettings.PreferNeighborAfterRemove(new List<string> { @"C:\A" }, 0));
    }

    [Fact]
    public void IsCoveredByAnyMappingRoot_CueFalseWhenMapped()
    {
        var notes = Path.Combine(Path.GetTempPath(), "ProtoLinkNmrCovered_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(notes);
        try
        {
            Assert.True(LocalPathCoverage.IsCoveredByAnyMappingRoot(notes, new[] { notes }));
            Assert.False(LocalPathCoverage.IsCoveredByAnyMappingRoot(notes, new[] { Path.Combine(notes, "child-only") }));
        }
        finally
        {
            try { Directory.Delete(notes, recursive: true); } catch { /* ignore */ }
        }
    }
}
