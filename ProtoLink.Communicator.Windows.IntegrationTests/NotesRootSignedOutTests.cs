using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Offline: Notes root choose/browse works without an auth session (#notes-without-login).
/// </summary>
[Trait("Category", "Offline")]
public class NotesRootSignedOutTests
{
    [Fact]
    public void SetNotesRootPath_WithoutAuth_EnablesLocalTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "ProtoLinkNotesSignedOut_" + Guid.NewGuid().ToString("N"));
        var note = Path.Combine(root, "Hello");
        Directory.CreateDirectory(note);
        File.WriteAllText(Path.Combine(note, "index.html"), "<h1>Hello</h1>");
        try
        {
            // No auth / share / sync dependencies — signed-out local Notes path.
            var settings = new MemorySettingsService(new AppSettings());
            var vm = new NotesViewModel(settings);

            Assert.True(vm.NeedsNotesRoot);

            vm.SetNotesRootPath(root);

            Assert.False(vm.NeedsNotesRoot);
            Assert.Equal(root, vm.RootPath);
            Assert.Equal(root, settings.LoadSettings().NotesRootPath);
            Assert.NotNull(vm.TreeRoot);
            Assert.Contains(vm.TreeRoot!.Children, c => c.Name == "Hello");
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
