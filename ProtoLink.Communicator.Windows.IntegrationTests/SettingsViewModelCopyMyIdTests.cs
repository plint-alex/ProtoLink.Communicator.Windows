using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Copy my ID in Settings account section (share-sheet-ux §4).</summary>
[Trait("Category", "Offline")]
public class SettingsViewModelCopyMyIdTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void MyUserIdText_WhenAuthenticated_IsGuidD()
    {
        var vm = CreateVm(authenticated: true, uiLanguage: "en-US");

        Assert.Equal(UserId.ToString("D"), vm.MyUserIdText);
        Assert.Equal("Copy my ID", vm.CopyMyIdLabel);
        Assert.True(vm.CopyMyIdCommand.CanExecute(null));
    }

    [Fact]
    public void MyUserIdText_WhenLoggedOut_IsNull_AndCommandDisabled()
    {
        var vm = CreateVm(authenticated: false, uiLanguage: null);

        Assert.Null(vm.MyUserIdText);
        Assert.False(vm.CopyMyIdCommand.CanExecute(null));
    }

    [Fact]
    public void CopyMyIdLabel_UsesRussianWhenUiLanguageRu()
    {
        var vm = CreateVm(authenticated: true, uiLanguage: "ru-RU");

        Assert.Equal("Скопировать мой ID", vm.CopyMyIdLabel);
        Assert.Equal("Отправьте это тем, кто хочет дать вам доступ", vm.CopyMyIdHint);
    }

    private static SettingsViewModel CreateVm(bool authenticated, string? uiLanguage)
    {
        var auth = new FakeAuthService(authenticated
            ? new TokenData { UserId = UserId, Login = "tester", AccessToken = "t" }
            : null);
        var settings = new MemorySettingsService(new AppSettings { UiLanguage = uiLanguage });
        return new SettingsViewModel(
            settings,
            auth,
            new FakeTokenService(),
            new HttpClient(),
            NullLogger.Instance);
    }

    private sealed class FakeTokenService : ITokenService
    {
        public void SaveToken(TokenData data) { }
        public TokenData? LoadToken() => null;
        public void ClearToken() { }
    }

    private sealed class FakeAuthService : IAuthService
    {
        private TokenData? _token;

        public FakeAuthService(TokenData? token) => _token = token;

        public bool IsAuthenticated => _token != null;
        public TokenData? CurrentToken => _token;

        public Task<LoginResult> LoginAsync(string login, string password) =>
            throw new NotSupportedException();

        public Task<RegisterResult> RegisterAsync(string email, string password, string lang) =>
            throw new NotSupportedException();

        public Task<bool> RefreshTokenAsync() => Task.FromResult(false);

        public void Logout() => _token = null;
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
