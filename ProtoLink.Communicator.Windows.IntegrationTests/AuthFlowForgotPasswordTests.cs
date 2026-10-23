using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>AuthFlow Login → web forgot-password URL (offline; no browser).</summary>
[Trait("Category", "Offline")]
public class AuthFlowForgotPasswordTests
{
    [Fact]
    public void BuildForgotPasswordUrl_UsesDefaultWhenBaseMissing()
    {
        Assert.Equal(
            "https://protolink.ru/forgot-password",
            PublicSitePaths.BuildForgotPasswordUrl(null));
        Assert.Equal(
            "https://protolink.ru/forgot-password",
            PublicSitePaths.BuildForgotPasswordUrl("  "));
    }

    [Fact]
    public void BuildForgotPasswordUrl_TrimsTrailingSlash()
    {
        Assert.Equal(
            "https://stand.example/forgot-password",
            PublicSitePaths.BuildForgotPasswordUrl("https://stand.example/"));
    }

    [Fact]
    public void TryBuildForgotPasswordUrl_UsesPublicSiteBaseUrl()
    {
        var settings = new MemorySettingsService(new AppSettings
        {
            PublicSiteBaseUrl = "https://protolink.ru/"
        });
        var vm = new AuthFlowViewModel(new StubAuth(), settings);
        Assert.Equal("https://protolink.ru/forgot-password", vm.TryBuildForgotPasswordUrl());
    }

    [Fact]
    public void TryBuildForgotPasswordUrl_DefaultsWithoutSettings()
    {
        var vm = new AuthFlowViewModel(new StubAuth());
        Assert.Equal(
            PublicSitePaths.BuildForgotPasswordUrl(EntityLinks.DefaultPublicSiteBaseUrl),
            vm.TryBuildForgotPasswordUrl());
    }

    [Fact]
    public void ForgotPasswordCommand_CanExecute_WhenNotBusy()
    {
        var vm = new AuthFlowViewModel(new StubAuth());
        Assert.True(vm.ForgotPasswordCommand.CanExecute(null));
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
