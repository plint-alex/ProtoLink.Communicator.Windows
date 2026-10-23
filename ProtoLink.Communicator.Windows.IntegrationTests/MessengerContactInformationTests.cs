using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Messenger Contact information — can-execute + URL format (offline; no browser).</summary>
[Trait("Category", "Offline")]
public class MessengerContactInformationTests
{
    private const string SiteBase = "https://protolink.ru/";

    [Fact]
    public void OpenContactInformationCommand_CanExecute_RequiresResolvedUserId()
    {
        using var http = new HttpClient { BaseAddress = new Uri("http://localhost/") };
        var vm = new MessengerViewModel(new StubAuth(), http);
        Assert.False(vm.OpenContactInformationCommand.CanExecute(null));

        var empty = new Contact { Id = Guid.Empty, Name = "x" };
        Assert.False(vm.OpenContactInformationCommand.CanExecute(empty));
        Assert.Null(vm.TryBuildContactInformationUrl(empty));

        var ok = new Contact { Id = Guid.Parse("8f3a1111-2222-3333-4444-555555555555"), Name = "Ada" };
        Assert.True(vm.OpenContactInformationCommand.CanExecute(ok));
        Assert.Equal(
            EntityLinks.Build(EntityLinks.DefaultPublicSiteBaseUrl, ok.Id),
            vm.TryBuildContactInformationUrl(ok));
    }

    [Fact]
    public void TryBuildContactInformationUrl_UsesPublicSiteBaseUrl()
    {
        using var http = new HttpClient { BaseAddress = new Uri("http://localhost/") };
        var settings = new MemorySettingsService(new AppSettings { PublicSiteBaseUrl = SiteBase });
        var vm = new MessengerViewModel(new StubAuth(), http, settings);
        var ok = new Contact { Id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), Name = "Bob" };
        Assert.Equal(
            "https://protolink.ru/0f8fad5b-d9cb-469f-a165-70867728950e",
            vm.TryBuildContactInformationUrl(ok));
    }

    private sealed class StubAuth : IAuthService
    {
        public bool IsAuthenticated => false;
        public TokenData? CurrentToken => null;

        public Task<LoginResult> LoginAsync(string login, string password) =>
            throw new NotSupportedException();

        public Task<RegisterResult> RegisterAsync(string email, string password, string lang) =>
            throw new NotSupportedException();

        public Task<bool> RefreshTokenAsync() => Task.FromResult(false);

        public void Logout() { }
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
