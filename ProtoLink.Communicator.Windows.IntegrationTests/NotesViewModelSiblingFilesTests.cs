using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>AT-N7/N8 ViewModel: ShowSiblingFiles + SiblingFiles collection after CurrentPagePath.</summary>
[Trait("Category", "Offline")]
public class NotesViewModelSiblingFilesTests
{
    [Fact]
    public void CurrentPagePath_WithSiblingFile_ShowsPanelAndListsFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ProtoLinkSiblingVm_" + Guid.NewGuid().ToString("N"));
        var note = Path.Combine(root, "WithSiblingFile");
        Directory.CreateDirectory(note);
        File.WriteAllText(Path.Combine(note, "index.html"), "<h1>With sibling</h1>");
        File.WriteAllText(Path.Combine(note, "sibling-verify.txt"), "hello");
        try
        {
            var vm = new NotesViewModel(new MemorySettingsService(new AppSettings { NotesRootPath = root }));
            Assert.False(vm.ShowSiblingFiles);

            vm.CurrentPagePath = note;
            // Path set no longer enumerates siblings on the UI thread (#notes-responsiveness).
            vm.RefreshSiblingFiles();

            Assert.True(vm.ShowSiblingFiles);
            Assert.Contains(vm.SiblingFiles, f => f.Name == "sibling-verify.txt");
            Assert.DoesNotContain(vm.SiblingFiles, f => f.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase));
            Assert.All(vm.SiblingFiles, f => Assert.Equal(FileSystemItemType.File, f.Item.Type));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CurrentPagePath_OnlyDirsAndIndex_HidesPanel()
    {
        var root = Path.Combine(Path.GetTempPath(), "ProtoLinkSiblingVmHide_" + Guid.NewGuid().ToString("N"));
        var note = Path.Combine(root, "OnlyDirs");
        var child = Path.Combine(note, "ChildNote");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(note, "index.html"), "<h1>Only dirs</h1>");
        File.WriteAllText(Path.Combine(child, "index.html"), "<h1>Child</h1>");
        try
        {
            var vm = new NotesViewModel(new MemorySettingsService(new AppSettings { NotesRootPath = root }));
            vm.CurrentPagePath = note;
            vm.RefreshSiblingFiles();

            Assert.False(vm.ShowSiblingFiles);
            Assert.Empty(vm.SiblingFiles);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void RefreshSiblingFiles_AfterFileAppears_ShowsPanel()
    {
        var root = Path.Combine(Path.GetTempPath(), "ProtoLinkSiblingVmRefresh_" + Guid.NewGuid().ToString("N"));
        var note = Path.Combine(root, "LateSibling");
        Directory.CreateDirectory(note);
        File.WriteAllText(Path.Combine(note, "index.html"), "<h1>Late</h1>");
        try
        {
            var vm = new NotesViewModel(new MemorySettingsService(new AppSettings { NotesRootPath = root }));
            vm.CurrentPagePath = note;
            Assert.False(vm.ShowSiblingFiles);

            File.WriteAllText(Path.Combine(note, "appeared.txt"), "x");
            vm.RefreshSiblingFiles();

            Assert.True(vm.ShowSiblingFiles);
            Assert.Contains(vm.SiblingFiles, f => f.Name == "appeared.txt");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    private sealed class MemorySettingsService : ISettingsService
    {
        private AppSettings _settings;

        public MemorySettingsService(AppSettings settings) => _settings = Clone(settings);

        public AppSettings LoadSettings() => Clone(_settings);

        public void SaveSettings(AppSettings settings) => _settings = Clone(settings);

        private static AppSettings Clone(AppSettings s) => new()
        {
            Theme = s.Theme,
            ApiBaseAddress = s.ApiBaseAddress,
            PublicSiteBaseUrl = s.PublicSiteBaseUrl,
            UiLanguage = s.UiLanguage,
            NotesRootPath = s.NotesRootPath,
            LastOpenCloudEntityId = s.LastOpenCloudEntityId
        };
    }
}
