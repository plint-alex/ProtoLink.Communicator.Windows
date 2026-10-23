using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Settings Account → Change password command (signed-in only).</summary>
[Trait("Category", "Offline")]
public class SettingsViewModelChangePasswordTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void ChangePasswordCommand_EnabledWhenAuthenticated()
    {
        var vm = CreateVm(authenticated: true, uiLanguage: "en-US");
        Assert.Equal("Change password", vm.ChangePasswordLabel);
        Assert.True(vm.ChangePasswordCommand.CanExecute(null));
    }

    [Fact]
    public void ChangePasswordCommand_DisabledWhenLoggedOut()
    {
        var vm = CreateVm(authenticated: false, uiLanguage: null);
        Assert.False(vm.ChangePasswordCommand.CanExecute(null));
    }

    [Fact]
    public void ChangePasswordLabel_UsesRussianWhenUiLanguageRu()
    {
        var vm = CreateVm(authenticated: true, uiLanguage: "ru-RU");
        Assert.Equal("Сменить пароль", vm.ChangePasswordLabel);
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

        public Task<ChangePasswordResult> ChangePasswordAsync(string currentPassword, string newPassword) =>
            Task.FromResult(new ChangePasswordResult { Success = true });

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
