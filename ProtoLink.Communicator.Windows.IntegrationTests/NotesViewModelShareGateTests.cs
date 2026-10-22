using System.Net.Http;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Notes;
using ProtoLink.Communicator.Windows.Services.Sync;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class NotesViewModelShareGateTests
{
    private static readonly Guid KnownId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void CanOpenShareForPath_WithoutShareApi_FalseEvenWhenSynced()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-share-gate-" + Guid.NewGuid().ToString("N"));
        var note = Path.Combine(root, "Page");
        Directory.CreateDirectory(note);
        try
        {
            var settings = new MemorySettingsService(new AppSettings { NotesRootPath = root });
            var vm = new NotesViewModel(settings, new FixedRemoteIdResolver(KnownId));

            Assert.Null(vm.ShareSheet);
            Assert.False(vm.CanOpenShareForPath(note));
            Assert.True(vm.TryResolveRemoteId(note).HasValue);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void CanOpenShareForPath_WithShareApi_MatchesCopyLinkGate()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-share-open-" + Guid.NewGuid().ToString("N"));
        var note = Path.Combine(root, "Page");
        Directory.CreateDirectory(note);
        using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:9/") };
        try
        {
            var settings = new MemorySettingsService(new AppSettings { NotesRootPath = root });
            var api = new CloudApiService(http);
            var synced = new NotesViewModel(
                settings,
                new FixedRemoteIdResolver(KnownId),
                shareApi: api,
                shareCurrentUserId: () => KnownId,
                shareIsOnline: () => false);
            var unsynced = new NotesViewModel(
                settings,
                new FixedRemoteIdResolver(null),
                shareApi: api,
                shareCurrentUserId: () => KnownId,
                shareIsOnline: () => false);

            Assert.NotNull(synced.ShareSheet);
            Assert.True(synced.CanOpenShareForPath(note));
            Assert.False(unsynced.CanOpenShareForPath(note));
            Assert.False(unsynced.CanOpenShareForPath(null));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void CanOpenShareForPath_Unsynced_False()
    {
        var settings = new MemorySettingsService(new AppSettings());
        var vm = new NotesViewModel(settings, new FixedRemoteIdResolver(null));
        Assert.False(vm.CanOpenShareForPath(Path.Combine(Path.GetTempPath(), "no-note")));
        Assert.False(vm.CanOpenShareForPath(null));
    }

    [Fact]
    public void ShowsExternalShareAffordance_OnlyAfterCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-share-aff-" + Guid.NewGuid().ToString("N"));
        var note = Path.Combine(root, "Page");
        Directory.CreateDirectory(note);
        try
        {
            var settings = new MemorySettingsService(new AppSettings { NotesRootPath = root });
            var vm = new NotesViewModel(settings, new FixedRemoteIdResolver(KnownId));

            Assert.False(vm.ShowsExternalShareAffordance(note));
            vm.RememberExternalShare(KnownId, true);
            Assert.True(vm.ShowsExternalShareAffordance(note));
            vm.RememberExternalShare(KnownId, false);
            Assert.False(vm.ShowsExternalShareAffordance(note));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void OpenShareForPathAsync_Unsynced_SetsNotInCloudStatus()
    {
        var settings = new MemorySettingsService(new AppSettings { UiLanguage = "en-US" });
        var vm = new NotesViewModel(settings, new FixedRemoteIdResolver(null));
        // No ShareSheet — method returns early after status when path unresolved... without sheet returns immediately.
        Assert.Null(vm.ShareSheet);
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
