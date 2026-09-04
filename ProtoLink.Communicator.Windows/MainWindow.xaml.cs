using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels;
using ProtoLink.Communicator.Windows.Views;

namespace ProtoLink.Communicator.Windows;

public partial class MainWindow : Window
{
    private readonly IAuthService _authService;
    private readonly ITokenService _tokenService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<MainWindow> _logger;
    private HttpClient _httpClient;
    private readonly NotesViewModel _notesViewModel;
    private readonly Views.NotesView _notesView;
    private CloudViewModel _cloudViewModel = null!;
    private readonly HttpClient _cloudHttpClient;
    private readonly SyncMappingStore _syncStore;
    private readonly object _unauthorizedLock = new();
    private bool _logoutUiQueued;
    private System.Windows.Controls.Panel? _tabToolbarPanel;
    private bool _developerToolsOpen;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"ProtoLink Communicator {AppVersionInfo.Current}";
        DeveloperToolsHost.CloseRequested += () => SetDeveloperToolsVisible(false);
        _logger = App.LoggerFactory.CreateLogger<MainWindow>();
        _tokenService = new TokenService(App.LoggerFactory.CreateLogger<TokenService>());
        _settingsService = new SettingsService(App.LoggerFactory.CreateLogger<SettingsService>());
        var settings = _settingsService.LoadSettings();
        _httpClient = new HttpClient(new GetEntitiesThrottlingHandler { InnerHandler = WrapWithDevToolsLogging(new HttpClientHandler()) }) { BaseAddress = new Uri(settings.ApiBaseAddress) };
        _authService = new AuthService(_httpClient, _tokenService, App.LoggerFactory.CreateLogger<AuthService>());
        ShowMessengerContent();
        _syncStore = new SyncMappingStore(App.LoggerFactory.CreateLogger<SyncMappingStore>());
        var cloudHandler = CreateAuthHandler();
        cloudHandler.InnerHandler = new GetEntitiesThrottlingHandler { InnerHandler = WrapWithDevToolsLogging(new HttpClientHandler()) };
        _cloudHttpClient = new HttpClient(cloudHandler) { BaseAddress = new Uri(settings.ApiBaseAddress) };
        RebuildCloudTab();
        _notesViewModel = new NotesViewModel(_settingsService, path => _cloudViewModel.RequestSyncForLocalPath(path));
        _notesView = new Views.NotesView { DataContext = _notesViewModel };
        NotesTabContent.Children.Add(_notesView);
    }

    private AuthHandler CreateAuthHandler()
        => new AuthHandler(
            _tokenService,
            App.LoggerFactory.CreateLogger<AuthHandler>(),
            OnApiUnauthorized,
            () => _authService.RefreshTokenAsync());

    private static DevToolsLoggingHandler WrapWithDevToolsLogging(HttpClientHandler inner)
        => new DevToolsLoggingHandler(inner);

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.F12)
            return;
        e.Handled = true;
        SetDeveloperToolsVisible(!_developerToolsOpen);
    }

    private void SetDeveloperToolsVisible(bool visible)
    {
        _developerToolsOpen = visible;
        if (visible)
        {
            RootLayout.RowDefinitions[1].Height = new GridLength(5);
            RootLayout.RowDefinitions[2].Height = new GridLength(220);
            RootLayout.RowDefinitions[2].MinHeight = 80;
            DevToolsSplitter.Visibility = Visibility.Visible;
            DeveloperToolsHost.Visibility = Visibility.Visible;
            DevToolsStore.AppendConsole("Developer tools (F12). Console: ILogger output. Network: HttpClient requests.");
        }
        else
        {
            RootLayout.RowDefinitions[1].Height = new GridLength(0);
            RootLayout.RowDefinitions[2].Height = new GridLength(0);
            RootLayout.RowDefinitions[2].MinHeight = 0;
            DevToolsSplitter.Visibility = Visibility.Collapsed;
            DeveloperToolsHost.Visibility = Visibility.Collapsed;
        }
    }

    private void OnApiUnauthorized()
    {
        _authService.Logout();
        lock (_unauthorizedLock)
        {
            if (_logoutUiQueued)
                return;
            _logoutUiQueued = true;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                CloseSettingsPanel();
                RebuildCloudTab();
                if (_tabToolbarPanel != null)
                    UpdateTabToolbar(_tabToolbarPanel);
            }
            finally
            {
                lock (_unauthorizedLock) { _logoutUiQueued = false; }
            }
        }));
    }

    private void RebuildCloudTab()
    {
        CloudTabContent.Children.Clear();
        _cloudViewModel = new CloudViewModel(
            _authService,
            _cloudHttpClient,
            _syncStore,
            _settingsService,
            ex =>
            {
                Dispatcher.InvokeAsync(() =>
                    ProtoLink.Communicator.Windows.Dialogs.ErrorDetailDialog.Show("Sync error", ex));
            },
            OpenSettingsForLogin);
        CloudTabContent.Children.Add(new CloudView { DataContext = _cloudViewModel });
    }

    private void OpenSettingsForLogin()
    {
        if (!_authService.IsAuthenticated)
            Settings_Click(this, new RoutedEventArgs());
    }

    private void MainTabControl_Loaded(object sender, RoutedEventArgs e)
    {
        var btn = MainTabControl.Template?.FindName("SettingsButtonInTemplate", MainTabControl) as System.Windows.Controls.Button;
        if (btn != null)
            btn.Click += (s2, e2) => Settings_Click(s2, e2);
        var toolbarPanel = MainTabControl.Template?.FindName("TabToolbarPanel", MainTabControl) as System.Windows.Controls.Panel;
        if (toolbarPanel != null)
        {
            _tabToolbarPanel = toolbarPanel;
            MainTabControl.SelectionChanged += (s2, e2) =>
            {
                UpdateTabToolbar(toolbarPanel);
                if (MainTabControl.SelectedIndex == 1)
                    _notesView.ActivateNotesTab();
            };
            UpdateTabToolbar(toolbarPanel);
            if (MainTabControl.SelectedIndex == 1)
                _notesView.ActivateNotesTab();
        }
    }

    private void UpdateTabToolbar(System.Windows.Controls.Panel toolbarPanel)
    {
        toolbarPanel.Children.Clear();
        var h = 22;
        var pad = new Thickness(6, 0, 6, 0);
        var margin = new Thickness(0, 0, 6, 0);
        if (MainTabControl.SelectedIndex == 1) // Notes
        {
            var newFolderBtn = new System.Windows.Controls.Button { Content = "New folder", Height = h, Padding = pad, Margin = margin };
            newFolderBtn.Click += (s, e) => _notesViewModel.RaiseRequestNewFolder();
            var refreshBtn = new System.Windows.Controls.Button { Content = "Refresh", Height = h, Padding = pad };
            refreshBtn.Click += (s, e) => _notesViewModel.RefreshCommand.Execute(null);
            toolbarPanel.Children.Add(newFolderBtn);
            toolbarPanel.Children.Add(refreshBtn);
            return;
        }
        if (MainTabControl.SelectedIndex == 2) // Cloud
        {
            var refreshBtn = new System.Windows.Controls.Button { Content = "Refresh", Height = h, Padding = pad, Margin = margin };
            refreshBtn.Click += (s, e) => _cloudViewModel.RefreshCommand.Execute(null);
            var newFolderBtn = new System.Windows.Controls.Button { Content = "New folder", Height = h, Padding = pad, Margin = margin };
            newFolderBtn.Click += (s, e) => _cloudViewModel.NewFolderCommand.Execute(null);
            var uploadBtn = new System.Windows.Controls.Button { Content = "Upload", Height = h, Padding = pad, Margin = margin };
            uploadBtn.Click += (s, e) => _cloudViewModel.UploadCommand.Execute(null);
            var downloadBtn = new System.Windows.Controls.Button { Content = "Download", Height = h, Padding = pad, Command = _cloudViewModel.DownloadCommand };
            var openWebBtn = new System.Windows.Controls.Button { Content = "Open in browser", Height = h, Padding = pad, Margin = margin, Command = _cloudViewModel.OpenWebExplorerCommand };
            toolbarPanel.Children.Add(refreshBtn);
            toolbarPanel.Children.Add(newFolderBtn);
            toolbarPanel.Children.Add(uploadBtn);
            toolbarPanel.Children.Add(downloadBtn);
            toolbarPanel.Children.Add(openWebBtn);
        }
    }

    private void CloseSettingsPanel()
    {
        SettingsPanel.Children.Clear();
        SettingsPanel.Visibility = Visibility.Collapsed;
        ShowMessengerContent();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsPanel.Children.Count == 0)
        {
            var settings = _settingsService.LoadSettings();
            var handler = CreateAuthHandler();
            handler.InnerHandler = new GetEntitiesThrottlingHandler { InnerHandler = WrapWithDevToolsLogging(new HttpClientHandler()) };
            var settingsClient = new HttpClient(handler) { BaseAddress = new Uri(settings.ApiBaseAddress) };
            var vm = new SettingsViewModel(_settingsService, _authService, _tokenService, settingsClient, _logger);
            vm.OnLoginSuccess += CloseSettingsPanel;
            vm.OnCloseRequested += CloseSettingsPanel;
            vm.OnContactAdded += CloseSettingsPanel;
            vm.OnSettingsSaved += () =>
            {
                var updated = _settingsService.LoadSettings();
                _httpClient = new HttpClient(new GetEntitiesThrottlingHandler { InnerHandler = WrapWithDevToolsLogging(new HttpClientHandler()) }) { BaseAddress = new Uri(updated.ApiBaseAddress) };
                _notesViewModel.ReloadFromSettings();
                ShowMessengerContent();
            };
            SettingsPanel.Children.Add(new SettingsView
            {
                DataContext = vm,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch
            });
        }
        SettingsPanel.Visibility = Visibility.Visible;
    }

    private void ShowMessengerContent()
    {
        MessengerTabContent.Children.Clear();
        if (!_authService.IsAuthenticated)
        {
            MessengerTabContent.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Please log in in Settings.",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });
            return;
        }
        var settings = _settingsService.LoadSettings();
        var handler = CreateAuthHandler();
        handler.InnerHandler = new GetEntitiesThrottlingHandler { InnerHandler = WrapWithDevToolsLogging(new HttpClientHandler()) };
        var client = new HttpClient(handler) { BaseAddress = new Uri(settings.ApiBaseAddress) };
        var vm = new MessengerViewModel(_authService, client);
        MessengerTabContent.Children.Add(new MessengerView { DataContext = vm });
    }
}
