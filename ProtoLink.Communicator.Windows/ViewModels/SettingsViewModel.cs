using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ProtoLink.Communicator.Windows.Dialogs;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IAuthService _authService;
    private readonly ITokenService _tokenService;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private string _apiBaseAddress;
    private string _publicSiteBaseUrl;
    private string? _notesRootPath;
    private string _theme;
    private string? _uiLanguage;
    private string _apiVersionText = "Loading…";
    private string? _copyMyIdFeedback;

    public IReadOnlyList<string> ThemeOptions { get; } = new[] { ThemeManager.Light, ThemeManager.Dark };

    public SettingsViewModel(ISettingsService settingsService, IAuthService authService, ITokenService tokenService, HttpClient httpClient, ILogger logger)
    {
        _settingsService = settingsService;
        _authService = authService;
        _tokenService = tokenService;
        _httpClient = httpClient;
        _logger = logger;
        var settings = _settingsService.LoadSettings();
        _apiBaseAddress = settings.ApiBaseAddress;
        _publicSiteBaseUrl = settings.PublicSiteBaseUrl;
        _notesRootPath = settings.NotesRootPath;
        _theme = ThemeManager.Normalize(settings.Theme);
        _uiLanguage = settings.UiLanguage;

        SaveCommand = new RelayCommand(_ => SaveSettings());
        LogOffCommand = new RelayCommand(_ => LogOff(), _ => _authService.IsAuthenticated);
        CloseCommand = new RelayCommand(_ => OnCloseRequested?.Invoke());
        RefreshApiVersionCommand = new RelayCommand(_ => _ = LoadApiVersionAsync());
        CopyMyIdCommand = new RelayCommand(_ => CopyMyId(), _ => !string.IsNullOrEmpty(MyUserIdText));
        ChangePasswordCommand = new RelayCommand(_ => ChangePassword(), _ => _authService.IsAuthenticated);

        LoginViewModel = new LoginViewModel(_authService);
        LoginViewModel.OnLoginSuccess += () =>
        {
            OnPropertyChanged(nameof(IsAuthenticated));
            OnPropertyChanged(nameof(CurrentLogin));
            OnPropertyChanged(nameof(MyUserIdText));
            OnPropertyChanged(nameof(CopyMyIdLabel));
            CommandManager.InvalidateRequerySuggested();
            OnLoginSuccess?.Invoke();
        };

        AddContactViewModel = new AddContactViewModel(_authService, _httpClient, _logger, isEmbedded: true);
        AddContactViewModel.OnContactAdded += () => OnContactAdded?.Invoke();

        _ = LoadApiVersionAsync();
    }

    public string AppVersion => AppVersionInfo.Current;
    public string ApiVersionText
    {
        get => _apiVersionText;
        private set { _apiVersionText = value; OnPropertyChanged(); }
    }

    public string ApiBaseAddress { get => _apiBaseAddress; set { _apiBaseAddress = value; OnPropertyChanged(); } }
    public string PublicSiteBaseUrl { get => _publicSiteBaseUrl; set { _publicSiteBaseUrl = value; OnPropertyChanged(); } }
    public string? NotesRootPath { get => _notesRootPath; set { _notesRootPath = value; OnPropertyChanged(); } }

    public string Theme
    {
        get => _theme;
        set
        {
            var n = ThemeManager.Normalize(value);
            if (_theme == n) return;
            _theme = n;
            OnPropertyChanged();
            ThemeManager.Apply(_theme);
        }
    }
    public bool IsAuthenticated => _authService.IsAuthenticated;
    public string? CurrentLogin => _authService.CurrentToken?.Login;
    /// <summary>User entity id in D format for paste into Share → Add people (same as web Copy my ID).</summary>
    public string? MyUserIdText =>
        _authService.CurrentToken is { UserId: var id } && id != Guid.Empty ? id.ToString("D") : null;
    public string CopyMyIdLabel =>
        string.IsNullOrEmpty(_copyMyIdFeedback)
            ? ShareAccountStrings.CopyMyIdLabel(_uiLanguage)
            : _copyMyIdFeedback;
    public string CopyMyIdHint => ShareAccountStrings.CopyMyIdHint(_uiLanguage);
    public string ChangePasswordLabel => ChangePasswordStrings.MenuLabel(_uiLanguage);
    public LoginViewModel LoginViewModel { get; }
    public AddContactViewModel AddContactViewModel { get; }
    public ICommand SaveCommand { get; }
    public ICommand LogOffCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand RefreshApiVersionCommand { get; }
    public ICommand CopyMyIdCommand { get; }
    public ICommand ChangePasswordCommand { get; }
    public event Action? OnLoginSuccess;
    public event Action? OnContactAdded;
    public event Action? OnSettingsSaved;
    public event Action? OnCloseRequested;

    private async Task LoadApiVersionAsync()
    {
        try
        {
            ApiVersionText = "Loading…";
            var apiBase = string.IsNullOrWhiteSpace(ApiBaseAddress)
                ? _httpClient.BaseAddress?.ToString()
                : ApiBaseAddress;
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                ApiBaseAddressPaths.BuildUnderApiRoot(apiBase, "Version"));
            var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                ApiVersionText = $"Unavailable (HTTP {(int)response.StatusCode})";
                return;
            }

            var parsed = JsonConvert.DeserializeObject<ServerVersionResponse>(body);
            var ver = AppVersionInfo.TruncateRevision(parsed?.Api?.Version ?? "");
            if (string.IsNullOrWhiteSpace(ver))
            {
                ApiVersionText = "Unavailable";
                return;
            }

            ApiVersionText = string.IsNullOrWhiteSpace(parsed?.Api?.BuildDate)
                ? ver
                : $"{ver} ({parsed!.Api!.BuildDate})";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load API version");
            ApiVersionText = "Unavailable";
        }
    }

    private void SaveSettings()
    {
        // Preserve fields not edited on this page (UiLanguage, LastOpenCloudEntityId).
        var existing = _settingsService.LoadSettings();
        _settingsService.SaveSettings(new AppSettings
        {
            Theme = Theme,
            ApiBaseAddress = ApiBaseAddress,
            PublicSiteBaseUrl = PublicSiteBaseUrl,
            NotesRootPath = NotesRootPath,
            UiLanguage = existing.UiLanguage,
            LastOpenCloudEntityId = existing.LastOpenCloudEntityId
        });
        OnSettingsSaved?.Invoke();
        _ = LoadApiVersionAsync();
    }

    private void LogOff()
    {
        _authService.Logout();
        _copyMyIdFeedback = null;
        OnPropertyChanged(nameof(IsAuthenticated));
        OnPropertyChanged(nameof(CurrentLogin));
        OnPropertyChanged(nameof(MyUserIdText));
        OnPropertyChanged(nameof(CopyMyIdLabel));
        OnSettingsSaved?.Invoke();
    }

    private void ChangePassword()
    {
        if (!_authService.IsAuthenticated) return;
        try
        {
            ChangePasswordDialog.TryChange(_authService, _uiLanguage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Change password dialog failed");
        }
    }

    private void CopyMyId()
    {
        if (MyUserIdText is not { } id) return;
        try
        {
            System.Windows.Clipboard.SetText(id);
            _copyMyIdFeedback = ShareAccountStrings.CopiedLabel(_uiLanguage);
            OnPropertyChanged(nameof(CopyMyIdLabel));
            _ = ClearCopyMyIdFeedbackAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not copy user id to clipboard");
            _copyMyIdFeedback = null;
            OnPropertyChanged(nameof(CopyMyIdLabel));
        }
    }

    private async Task ClearCopyMyIdFeedbackAsync()
    {
        await Task.Delay(2000);
        _copyMyIdFeedback = null;
        OnPropertyChanged(nameof(CopyMyIdLabel));
    }
}
