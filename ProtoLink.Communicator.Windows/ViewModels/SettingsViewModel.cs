using System.Collections.Generic;
using System.Net.Http;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;

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

        SaveCommand = new RelayCommand(_ => SaveSettings());
        LogOffCommand = new RelayCommand(_ => LogOff(), _ => _authService.IsAuthenticated);
        CloseCommand = new RelayCommand(_ => OnCloseRequested?.Invoke());

        LoginViewModel = new LoginViewModel(_authService);
        LoginViewModel.OnLoginSuccess += () => OnLoginSuccess?.Invoke();

        AddContactViewModel = new AddContactViewModel(_authService, _httpClient, _logger, isEmbedded: true);
        AddContactViewModel.OnContactAdded += () => OnContactAdded?.Invoke();
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
    public LoginViewModel LoginViewModel { get; }
    public AddContactViewModel AddContactViewModel { get; }
    public ICommand SaveCommand { get; }
    public ICommand LogOffCommand { get; }
    public ICommand CloseCommand { get; }
    public event Action? OnLoginSuccess;
    public event Action? OnContactAdded;
    public event Action? OnSettingsSaved;
    public event Action? OnCloseRequested;

    private void SaveSettings()
    {
        _settingsService.SaveSettings(new AppSettings
        {
            Theme = Theme,
            ApiBaseAddress = ApiBaseAddress,
            PublicSiteBaseUrl = PublicSiteBaseUrl,
            NotesRootPath = NotesRootPath
        });
        OnSettingsSaved?.Invoke();
    }

    private void LogOff()
    {
        _authService.Logout();
        OnPropertyChanged(nameof(IsAuthenticated));
        OnPropertyChanged(nameof(CurrentLogin));
        OnSettingsSaved?.Invoke();
    }
}
