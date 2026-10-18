using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class NotesViewModelRootStateTests
{
    [Fact]
    public void UnsetRoot_ShowsSetupNotEmptyNotes()
    {
        var settings = new MemorySettingsService(new AppSettings { NotesRootPath = null });
        var vm = new NotesViewModel(settings);

        Assert.True(vm.NeedsNotesRoot);
        Assert.False(vm.IsNotesEmpty);
        Assert.False(vm.ShowNotesTree);
        Assert.False(vm.CreateFirstNoteCommand.CanExecute(null));
        Assert.True(vm.ChooseNotesRootCommand.CanExecute(null));
    }

    [Fact]
    public void MissingRootFolder_ShowsSetup()
    {
        var missing = Path.Combine(Path.GetTempPath(), "ProtoLinkNotesMissing_" + Guid.NewGuid().ToString("N"));
        var settings = new MemorySettingsService(new AppSettings { NotesRootPath = missing });
        var vm = new NotesViewModel(settings);

        Assert.True(vm.NeedsNotesRoot);
        Assert.False(vm.IsNotesEmpty);
        Assert.False(vm.ShowNotesTree);
    }

    [Fact]
    public void SetNotesRootPath_PersistsAndClearsSetupFlag()
    {
        var root = Path.Combine(Path.GetTempPath(), "ProtoLinkNotesRoot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = new MemorySettingsService(new AppSettings());
            var vm = new NotesViewModel(settings);
            Assert.True(vm.NeedsNotesRoot);

            vm.SetNotesRootPath(root);

            Assert.Equal(Path.GetFullPath(root), settings.LoadSettings().NotesRootPath);
            Assert.False(vm.NeedsNotesRoot);
            Assert.Equal(Path.GetFullPath(root), vm.RootPath);
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
