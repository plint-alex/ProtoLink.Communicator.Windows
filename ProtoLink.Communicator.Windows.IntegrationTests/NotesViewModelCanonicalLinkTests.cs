using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Notes;
using ProtoLink.Communicator.Windows.Services.Sync;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class NotesViewModelCanonicalLinkTests
{
    private static readonly Guid KnownId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private const string SiteBase = "https://protolink.ru";

    [Fact]
    public void TryBuildCanonicalLink_SyncedPath_ReturnsUrlForThatNote()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-link-" + Guid.NewGuid().ToString("N"));
        var note = Path.Combine(root, "Page");
        Directory.CreateDirectory(note);
        try
        {
            var settings = new MemorySettingsService(new AppSettings
            {
                NotesRootPath = root,
                PublicSiteBaseUrl = SiteBase
            });
            var vm = new NotesViewModel(settings, new FixedRemoteIdResolver(KnownId));

            var url = vm.TryBuildCanonicalLink(note);

            Assert.Equal(EntityLinks.Build(SiteBase, KnownId), url);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void TryBuildCanonicalLink_UnmappedPath_ReturnsNull()
    {
        var settings = new MemorySettingsService(new AppSettings { PublicSiteBaseUrl = SiteBase });
        var vm = new NotesViewModel(settings, new FixedRemoteIdResolver(null));

        Assert.Null(vm.TryBuildCanonicalLink(Path.Combine(Path.GetTempPath(), "no-such-note")));
        Assert.Null(vm.TryBuildCanonicalLink(null));
        Assert.Null(vm.TryBuildCanonicalLink(""));
    }

    [Fact]
    public void TryBuildCanonicalLink_OpenPageUsesCurrentRemoteId()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-link-open-" + Guid.NewGuid().ToString("N"));
        var note = Path.Combine(root, "OpenPage");
        Directory.CreateDirectory(note);
        try
        {
            var settings = new MemorySettingsService(new AppSettings
            {
                NotesRootPath = root,
                PublicSiteBaseUrl = SiteBase
            });
            var vm = new NotesViewModel(settings, new FixedRemoteIdResolver(KnownId));
            vm.CurrentPagePath = note;
            vm.RecordOpenedPage(null, note, pushHistory: false);

            Assert.Equal(EntityLinks.Build(SiteBase, KnownId), vm.TryBuildCanonicalLink(null));
            Assert.True(vm.CopyLinkCommand.CanExecute(null));
            Assert.True(vm.OpenInBrowserCommand.CanExecute(null));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void CopyLinkCommand_CanExecute_ForSyncedTreePathParameter()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-link-cmd-" + Guid.NewGuid().ToString("N"));
        var synced = Path.Combine(root, "Synced");
        var unsynced = Path.Combine(root, "LocalOnly");
        Directory.CreateDirectory(synced);
        Directory.CreateDirectory(unsynced);
        try
        {
            var settings = new MemorySettingsService(new AppSettings
            {
                NotesRootPath = root,
                PublicSiteBaseUrl = SiteBase
            });
            var map = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase)
            {
                [synced] = KnownId,
                [unsynced] = null
            };
            var vm = new NotesViewModel(settings, new MapRemoteIdResolver(map));

            Assert.True(vm.CopyLinkCommand.CanExecute(synced));
            Assert.True(vm.OpenInBrowserCommand.CanExecute(synced));
            Assert.False(vm.CopyLinkCommand.CanExecute(unsynced));
            Assert.False(vm.OpenInBrowserCommand.CanExecute(unsynced));
            Assert.False(vm.CopyLinkCommand.CanExecute(null));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void CopyLinkForPath_Unmapped_SetsNotInCloudStatus()
    {
        var settings = new MemorySettingsService(new AppSettings { PublicSiteBaseUrl = SiteBase });
        var vm = new NotesViewModel(settings, new FixedRemoteIdResolver(null));

        vm.CopyLinkForPath(Path.Combine(Path.GetTempPath(), "unsynced"));

        Assert.Equal("Not in Cloud yet", vm.StatusText);
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

    private sealed class MapRemoteIdResolver : INoteRemoteIdResolver
    {
        private readonly Dictionary<string, Guid?> _map;

        public MapRemoteIdResolver(Dictionary<string, Guid?> map) => _map = map;

        public Guid? Resolve(string noteFolderFullPath)
        {
            if (string.IsNullOrWhiteSpace(noteFolderFullPath)) return null;
            return _map.TryGetValue(noteFolderFullPath, out var id) ? id : null;
        }

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
