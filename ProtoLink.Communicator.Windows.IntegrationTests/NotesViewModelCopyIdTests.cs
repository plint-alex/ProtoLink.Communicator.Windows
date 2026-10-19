using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Notes;
using ProtoLink.Communicator.Windows.Services.Sync;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class NotesViewModelCopyIdTests
{
    private static readonly Guid KnownId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void TryResolveRemoteId_SyncedPath_ReturnsGuid()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-copyid-" + Guid.NewGuid().ToString("N"));
        var note = Path.Combine(root, "Page");
        Directory.CreateDirectory(note);
        try
        {
            var settings = new MemorySettingsService(new AppSettings { NotesRootPath = root });
            var vm = new NotesViewModel(settings, new FixedRemoteIdResolver(KnownId));

            Assert.Equal(KnownId, vm.TryResolveRemoteId(note));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void TryResolveRemoteId_Unmapped_ReturnsNull()
    {
        var settings = new MemorySettingsService(new AppSettings());
        var vm = new NotesViewModel(settings, new FixedRemoteIdResolver(null));

        Assert.Null(vm.TryResolveRemoteId(Path.Combine(Path.GetTempPath(), "no-such-note")));
        Assert.Null(vm.TryResolveRemoteId(null));
        Assert.Null(vm.TryResolveRemoteId(""));
    }

    [Fact]
    public void TryResolveRemoteId_WithoutResolver_ReturnsNull()
    {
        var settings = new MemorySettingsService(new AppSettings());
        var vm = new NotesViewModel(settings);

        Assert.Null(vm.TryResolveRemoteId(Path.Combine(Path.GetTempPath(), "any")));
    }

    private sealed class FixedRemoteIdResolver : INoteRemoteIdResolver
    {
        private readonly Guid? _id;

        public FixedRemoteIdResolver(Guid? id) => _id = id;

        public Guid? Resolve(string noteFolderFullPath) =>
            string.IsNullOrWhiteSpace(noteFolderFullPath) ? null : _id;

        public Guid? ResolveFrom(
            string noteFolderFullPath,
            IReadOnlyList<SyncMappingInfo> mappings,
            IReadOnlyList<SyncItemMeta> items) =>
            Resolve(noteFolderFullPath);
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
