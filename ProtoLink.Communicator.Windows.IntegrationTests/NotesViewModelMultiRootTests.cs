using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class NotesViewModelMultiRootTests
{
    [Fact]
    public void AddSecondRoot_KeepsBoth_SelectsNew()
    {
        var a = CreateTempRoot("A");
        var b = CreateTempRoot("B");
        try
        {
            var settings = new MemorySettingsService(new AppSettings { NotesRootPath = a });
            var vm = new NotesViewModel(settings);

            Assert.Single(vm.NotesRoots);
            vm.AddNotesRootPath(b);

            Assert.Equal(2, vm.NotesRoots.Count);
            Assert.True(NotesRootSettings.PathsEqual(vm.RootPath, b));
            var saved = settings.LoadSettings();
            Assert.Equal(2, NotesRootSettings.ResolveRoots(saved).Count);
            Assert.True(NotesRootSettings.PathsEqual(saved.LastSelectedNotesRootPath, b));
            Assert.True(Directory.Exists(a));
            Assert.True(Directory.Exists(b));
        }
        finally
        {
            Wipe(a);
            Wipe(b);
        }
    }

    [Fact]
    public void RemoveRoot_DoesNotDeleteDisk_SelectsNeighbor()
    {
        var a = CreateTempRoot("A");
        var b = CreateTempRoot("B");
        try
        {
            var settings = new MemorySettingsService(new AppSettings());
            var vm = new NotesViewModel(settings);
            vm.AddNotesRootPath(a);
            vm.AddNotesRootPath(b);
            Assert.Equal(2, vm.NotesRoots.Count);

            vm.RemoveNotesRootPath(b);

            Assert.Single(vm.NotesRoots);
            Assert.True(NotesRootSettings.PathsEqual(vm.RootPath, a));
            Assert.True(Directory.Exists(b), "Remove must not delete disk");
            Assert.DoesNotContain(
                NotesRootSettings.ResolveRoots(settings.LoadSettings()),
                r => NotesRootSettings.PathsEqual(r, b));
        }
        finally
        {
            Wipe(a);
            Wipe(b);
        }
    }

    [Fact]
    public void SelectRoot_PersistsLastSelected_KeepsOpenPage_E11a()
    {
        var a = CreateTempRoot("A");
        var b = CreateTempRoot("B");
        var pageA = Path.Combine(a, "PageA");
        Directory.CreateDirectory(pageA);
        File.WriteAllText(Path.Combine(pageA, "index.html"), "<h1>A</h1>");
        try
        {
            var settings = new MemorySettingsService(new AppSettings());
            var vm = new NotesViewModel(settings);
            vm.AddNotesRootPath(a);
            vm.AddNotesRootPath(b);
            vm.SelectNotesRootPath(a);
            vm.CurrentPagePath = pageA;

            vm.SelectNotesRootPath(b);

            Assert.Equal(pageA, vm.CurrentPagePath);
            Assert.True(NotesRootSettings.PathsEqual(vm.RootPath, b));
            Assert.True(NotesRootSettings.PathsEqual(
                settings.LoadSettings().LastSelectedNotesRootPath, b));
        }
        finally
        {
            Wipe(a);
            Wipe(b);
        }
    }

    [Fact]
    public void StackedTrees_BuildAllRoots_AndPersistLastOpenPage()
    {
        var a = CreateTempRoot("StackA");
        var b = CreateTempRoot("StackB");
        var pageA = Path.Combine(a, "PageA");
        var pageB = Path.Combine(b, "PageB");
        Directory.CreateDirectory(pageA);
        Directory.CreateDirectory(pageB);
        File.WriteAllText(Path.Combine(pageA, "index.html"), "<h1>A</h1>");
        File.WriteAllText(Path.Combine(pageB, "index.html"), "<h1>B</h1>");
        try
        {
            var settings = new MemorySettingsService(new AppSettings());
            var vm = new NotesViewModel(settings);
            vm.AddNotesRootPath(a);
            vm.AddNotesRootPath(b);
            Assert.Equal(2, vm.TreeRoots.Count);

            vm.PersistLastOpenPage(pageB);
            Assert.True(NotesRootSettings.PathsEqual(
                settings.LoadSettings().LastOpenNotesPagePath, pageB));
            Assert.True(NotesRootSettings.PathsEqual(vm.RootPath, b));

            var restored = new NotesViewModel(settings);
            Assert.True(NotesRootSettings.PathsEqual(restored.CurrentPagePath, pageB));
            Assert.Equal(2, restored.TreeRoots.Count);
        }
        finally
        {
            Wipe(a);
            Wipe(b);
        }
    }

    [Fact]
    public void MigrateLegacy_OnLoad_WritesMultiRootShape()
    {
        var root = CreateTempRoot("Legacy");
        try
        {
            var settings = new MemorySettingsService(new AppSettings { NotesRootPath = root });
            _ = new NotesViewModel(settings);
            var saved = settings.LoadSettings();
            Assert.Single(saved.NotesRootPaths);
            Assert.True(NotesRootSettings.PathsEqual(saved.NotesRootPaths[0], root));
            Assert.True(NotesRootSettings.PathsEqual(saved.LastSelectedNotesRootPath, root));
        }
        finally
        {
            Wipe(root);
        }
    }

    [Fact]
    public void UnmappedRoot_ShowsNotInCloudCue()
    {
        var root = CreateTempRoot("Unmapped");
        try
        {
            var settings = new MemorySettingsService(new AppSettings());
            var vm = new NotesViewModel(settings);
            vm.AddNotesRootPath(root);

            Assert.True(vm.ShowMapToCloudNudge);
            Assert.Single(vm.NotesRoots);
            Assert.True(vm.NotesRoots[0].ShowNotInCloudCue);
            Assert.Contains("Not in Cloud yet", vm.NotesRoots[0].ToolTip, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Wipe(root);
        }
    }

    [Fact]
    public void AddRemoveSelect_WorksWithoutAuth()
    {
        var a = CreateTempRoot("SO_A");
        var b = CreateTempRoot("SO_B");
        try
        {
            var settings = new MemorySettingsService(new AppSettings());
            var vm = new NotesViewModel(settings);
            vm.AddNotesRootPath(a);
            vm.AddNotesRootPath(b);
            vm.SelectNotesRootPath(a);
            vm.RemoveNotesRootPath(b);
            Assert.Single(vm.NotesRoots);
            Assert.False(vm.NeedsNotesRoot);
            Assert.True(Directory.Exists(b));
        }
        finally
        {
            Wipe(a);
            Wipe(b);
        }
    }

    private static string CreateTempRoot(string label)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ProtoLinkNmr_{label}_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.GetFullPath(root);
    }

    private static void Wipe(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { /* ignore */ }
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
            NotesRootPaths = s.NotesRootPaths?.ToList() ?? new List<string>(),
            LastSelectedNotesRootPath = s.LastSelectedNotesRootPath,
            LastOpenNotesPagePath = s.LastOpenNotesPagePath,
            LastOpenCloudEntityId = s.LastOpenCloudEntityId
        };
    }
}
