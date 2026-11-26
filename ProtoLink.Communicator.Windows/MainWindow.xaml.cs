using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Utilities;
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
    private MessengerViewModel? _messengerViewModel;
    /// <summary>Cloud UI browse (Interactive lane) — Photos/folder list must not wait on sync.</summary>
    private readonly HttpClient _cloudUiHttpClient;
    /// <summary>Mapped sync + Notes share API (Sync lane / throttled GetEntities).</summary>
    private readonly HttpClient _cloudHttpClient;
    private readonly SyncMappingStore _syncStore;
    private readonly SignalRService _realtime = new();
    private readonly object _unauthorizedLock = new();
    private bool _logoutUiQueued;
    private System.Windows.Controls.Panel? _tabToolbarPanel;
    private bool _developerToolsOpen;
    private bool _authTabRedirecting;
    private int _realtimeRefreshBusy;
    private int _realtimeRefreshPending;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"ProtoLink Communicator {AppVersionInfo.Current}";
        DeveloperToolsHost.CloseRequested += () => SetDeveloperToolsVisible(false);
        _logger = App.LoggerFactory.CreateLogger<MainWindow>();
        _tokenService = new TokenService(App.LoggerFactory.CreateLogger<TokenService>());
        _settingsService = new SettingsService(App.LoggerFactory.CreateLogger<SettingsService>());
        var settings = _settingsService.LoadSettings();
        _httpClient = new HttpClient(new GetEntitiesThrottlingHandler(GetEntitiesThrottlingHandler.Lane.Interactive)
        {
            InnerHandler = WrapWithDevToolsLogging(new HttpClientHandler())
        }) { BaseAddress = new Uri(settings.ApiBaseAddress) };
        _authService = new AuthService(_httpClient, _tokenService, App.LoggerFactory.CreateLogger<AuthService>());
        _realtime.RefreshRequested += OnRealtimeRefreshRequested;
        // Cold start: signed-in → Messenger main tab; unsigned → Notes (#messenger-startup-priority).
        MainTabControl.SelectedIndex = AuthShellGate.ResolveInitialTab(_authService.IsAuthenticated);
        ShowMessengerContent();
        UpdateAuthGate();
        _syncStore = new SyncMappingStore(App.LoggerFactory.CreateLogger<SyncMappingStore>());
        var cloudUiHandler = CreateAuthHandler();
        cloudUiHandler.InnerHandler = new GetEntitiesThrottlingHandler(GetEntitiesThrottlingHandler.Lane.Interactive)
        {
            InnerHandler = WrapWithDevToolsLogging(new HttpClientHandler())
        };
        // Interactive listing uses values-light + InteractiveListingPageSize; 90s gives cold-start
        // headroom without waiting forever on a blackholed GetEntities (default HttpClient is 100s).
        _cloudUiHttpClient = new HttpClient(cloudUiHandler)
        {
            BaseAddress = new Uri(settings.ApiBaseAddress),
            Timeout = TimeSpan.FromSeconds(90)
        };
        var cloudSyncHandler = CreateAuthHandler();
        cloudSyncHandler.InnerHandler = new GetEntitiesThrottlingHandler(GetEntitiesThrottlingHandler.Lane.Sync)
        {
            InnerHandler = WrapWithDevToolsLogging(new HttpClientHandler())
        };
        // Explicit timeout (default 100s) so a blackholed GetEntities/getFile cannot hang
        // exclusive sync forever; coordinator still distinguishes preempt cancel via CTS.
        _cloudHttpClient = new HttpClient(cloudSyncHandler)
        {
            BaseAddress = new Uri(settings.ApiBaseAddress),
            Timeout = TimeSpan.FromSeconds(100)
        };
        var noteRemoteIdResolver = new ProtoLink.Communicator.Windows.Services.Notes.NoteRemoteIdResolver(_syncStore);
        var notesShareApi = new CloudApiService(_cloudHttpClient);
        _notesViewModel = new NotesViewModel(
            _settingsService,
            noteRemoteIdResolver,
            _syncStore,
            notesShareApi,
            () => _authService.CurrentToken?.UserId,
            () => _cloudViewModel == null || !_cloudViewModel.ShowNetworkBanner);
        RebuildCloudTab();
        _notesView = new Views.NotesView { DataContext = _notesViewModel };
        _notesViewModel.ContentSaved += _ => _cloudViewModel.RequestLocalPushAfterNoteSave();
        _notesViewModel.RequestAddNotesRootToCloud += path => _ = OpenAddLocalFolderWizardFromNotesAsync(path);
        NotesTabContent.Children.Add(_notesView);
        _ = EnsureRealtimeConnectedAsync();
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

    private void Window_Activated(object sender, EventArgs e)
    {
        if (_messengerViewModel != null)
            _messengerViewModel.IsAppForeground = true;
        // AT-N12: restore Notes airspace Popups that still have desired-open state.
        _notesView.ResumeAirspacePopupsForForeground();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_messengerViewModel != null)
            _messengerViewModel.IsAppForeground = false;
        // AT-N12 / E5b: close Popup HWNDs so Folder files / format bar / Share sheet
        // do not stay desktop-topmost over other apps.
        _notesView.SuspendAirspacePopupsForBackground();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            _cloudViewModel?.StopAutoSync();
            _notesView?.ShutdownForAppExit();
            foreach (var child in CloudTabContent.Children)
            {
                if (child is CloudView cloud)
                    cloud.ShutdownForAppExit();
            }
            _ = _realtime.DisconnectAsync();
            TaskbarUnreadBadge.Apply(this, 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Shutdown cleanup failed");
        }
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
                UpdateAuthGate();
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
        _cloudViewModel?.StopAutoSync();
        _cloudViewModel = new CloudViewModel(
            _authService,
            _cloudUiHttpClient,
            _syncStore,
            _settingsService,
            ex =>
            {
                // Auto-sync / mapped sync: connectivity refuse is expected offline — status already set; no modal spam.
                if (ApiExceptionHelper.IsExpectedConnectivityFailure(ex))
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                    return;
                }

                Dispatcher.InvokeAsync(() =>
                    ProtoLink.Communicator.Windows.Dialogs.ErrorDetailDialog.Show("Sync error", ex));
            },
            OpenSettingsForLogin,
            () =>
            {
                _notesViewModel?.RefreshTree();
                _notesViewModel?.RefreshRemoteLinkState();
                _notesViewModel?.RefreshMapToCloudNudge();
            },
            syncHttpClient: _cloudHttpClient);
        CloudTabContent.Children.Add(new CloudView { DataContext = _cloudViewModel });
        _notesViewModel?.RefreshMapToCloudNudge();
    }

    private async Task OpenAddLocalFolderWizardFromNotesAsync(string notesRootPath)
    {
        if (!_authService.IsAuthenticated)
        {
            OpenSettingsForLogin();
            return;
        }

        MainTabControl.SelectedIndex = 2; // Cloud
        if (_tabToolbarPanel != null)
            UpdateTabToolbar(_tabToolbarPanel);
        await _cloudViewModel.RunAddLocalFolderWizardAsync(notesRootPath);
        _notesViewModel.RefreshMapToCloudNudge();
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
                if (_authTabRedirecting)
                    return;

                // Notes-without-login: Messenger/Cloud stay login-gated.
                if (AuthShellGate.ShouldPromptLoginForTab(
                        _authService.IsAuthenticated, MainTabControl.SelectedIndex))
                {
                    _authTabRedirecting = true;
                    try
                    {
                        MainTabControl.SelectedIndex = AuthShellGate.UnsignedDefaultTabIndex;
                    }
                    finally
                    {
                        _authTabRedirecting = false;
                    }
                    OpenSettingsForLogin();
                    UpdateTabToolbar(toolbarPanel);
                    _notesView.ActivateNotesTab();
                    return;
                }

                // Toolbar + selection chrome first; Notes activate defers tree/WebView2
                // (#shell-tab-responsiveness / change-then-wait).
                UpdateTabToolbar(toolbarPanel);
                if (MainTabControl.SelectedIndex == AuthShellGate.NotesTabIndex)
                    _notesView.ActivateNotesTab();
            };
            ApplyUnsignedDefaultTabIfNeeded();
            UpdateTabToolbar(toolbarPanel);
            if (MainTabControl.SelectedIndex == AuthShellGate.NotesTabIndex)
                _notesView.ActivateNotesTab();
        }
    }

    private void UpdateTabToolbar(System.Windows.Controls.Panel toolbarPanel)
    {
        // Chrome-minimal CM-TT1…TT4: Notes/Cloud create/Download/Open stay off the permanent strip.
        // E11a: Notes-tab Add/Remove/Map roots sit adjacent to the Settings gear.
        toolbarPanel.Children.Clear();
        var actionIds = ShellTabToolbarChrome.PermanentActionIds(MainTabControl.SelectedIndex);
        foreach (var id in actionIds)
        {
            if (!ShellTabToolbarChrome.IsNotesRootChromeAction(id))
                throw new InvalidOperationException($"Shell TabToolbar action '{id}' is not wired; extend UpdateTabToolbar.");

            toolbarPanel.Children.Add(CreateNotesRootChromeButton(id));
        }
    }

    private System.Windows.Controls.Button CreateNotesRootChromeButton(string actionId)
    {
        var button = new System.Windows.Controls.Button
        {
            MinWidth = 36,
            Height = 36,
            Margin = new Thickness(0, 0, 4, 0),
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
            Foreground = (System.Windows.Media.Brush)FindResource("ForegroundBrush")
        };

        switch (actionId)
        {
            case ShellTabToolbarChrome.NotesAddRootActionId:
                button.Content = "+";
                button.ToolTip = "Add Notes folder";
                System.Windows.Automation.AutomationProperties.SetName(button, "Add Notes folder");
                button.Command = _notesViewModel.AddNotesRootCommand;
                break;
            case ShellTabToolbarChrome.NotesRemoveRootActionId:
                button.Content = "−";
                button.ToolTip = "Remove from Notes list (keeps files on disk)";
                System.Windows.Automation.AutomationProperties.SetName(button, "Remove Notes folder from list");
                button.Command = _notesViewModel.RemoveNotesRootCommand;
                break;
            case ShellTabToolbarChrome.NotesMapRootActionId:
                button.Content = "☁";
                button.ToolTip = "Map Notes folder to Cloud";
                System.Windows.Automation.AutomationProperties.SetName(button, "Map Notes root to Cloud");
                button.Command = _notesViewModel.MapSelectedNotesRootCommand;
                break;
            default:
                throw new InvalidOperationException($"Unknown Notes root chrome action '{actionId}'.");
        }

        return button;
    }

    private void CloseSettingsPanel()
    {
        SettingsPanel.Children.Clear();
        SettingsPanel.Visibility = Visibility.Collapsed;
        MainTabControl.Visibility = Visibility.Visible;
        ShowMessengerContent();
        UpdateAuthGate();
    }

    private void UpdateAuthGate()
    {
        // Notes-without-login: never collapse MainTabControl behind AuthFlow when unsigned.
        AuthFlowPanel.Children.Clear();
        AuthFlowPanel.Visibility = Visibility.Collapsed;

        if (SettingsPanel.Visibility != Visibility.Visible)
            MainTabControl.Visibility = Visibility.Visible;

        ApplyUnsignedDefaultTabIfNeeded();

        if (_tabToolbarPanel != null)
            UpdateTabToolbar(_tabToolbarPanel);
    }

    private void ApplyUnsignedDefaultTabIfNeeded()
    {
        if (AuthShellGate.HideTabsUntilAuthenticated)
            return;

        var resolved = AuthShellGate.ResolveTabAfterAuthChange(
            _authService.IsAuthenticated, MainTabControl.SelectedIndex);
        if (resolved == MainTabControl.SelectedIndex)
            return;

        _authTabRedirecting = true;
        try
        {
            MainTabControl.SelectedIndex = resolved;
        }
        finally
        {
            _authTabRedirecting = false;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsPanel.Visibility == Visibility.Visible)
        {
            CloseSettingsPanel();
            return;
        }

        // Change chrome first (hide tabs / show Settings host), then build content
        // (#shell-tab-responsiveness / change-then-wait).
        MainTabControl.Visibility = Visibility.Collapsed;
        AuthFlowPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;

        if (SettingsPanel.Children.Count > 0)
            return;

        ShellTabActivation.ScheduleAfterChromePaint(Dispatcher, EnsureSettingsContent);
    }

    private void EnsureSettingsContent()
    {
        if (SettingsPanel.Children.Count > 0)
            return;

        // While logged out, settings gear opens API/theme + login; tabs stay available for Notes.
        var settings = _settingsService.LoadSettings();
        var handler = CreateAuthHandler();
        handler.InnerHandler = new GetEntitiesThrottlingHandler(GetEntitiesThrottlingHandler.Lane.Interactive)
        {
            InnerHandler = WrapWithDevToolsLogging(new HttpClientHandler())
        };
        var settingsClient = new HttpClient(handler) { BaseAddress = new Uri(settings.ApiBaseAddress) };
        var vm = new SettingsViewModel(_settingsService, _authService, _tokenService, settingsClient, _logger);
        vm.OnLoginSuccess += () =>
        {
            // After login land on Messenger (main signed-in tab); then rebuild content.
            MainTabControl.SelectedIndex = AuthShellGate.SignedInDefaultTabIndex;
            CloseSettingsPanel();
            _ = EnsureRealtimeConnectedAsync();
        };
        vm.OnCloseRequested += CloseSettingsPanel;
        vm.OnContactAdded += CloseSettingsPanel;
        vm.OnSettingsSaved += () =>
        {
            var updated = _settingsService.LoadSettings();
            _httpClient = new HttpClient(new GetEntitiesThrottlingHandler(GetEntitiesThrottlingHandler.Lane.Interactive)
            {
                InnerHandler = WrapWithDevToolsLogging(new HttpClientHandler())
            }) { BaseAddress = new Uri(updated.ApiBaseAddress) };
            _notesViewModel.ReloadFromSettings();
            ShowMessengerContent();
            UpdateAuthGate();
        };
        SettingsPanel.Children.Add(new SettingsView
        {
            DataContext = vm,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch
        });
    }

    private void OnRealtimeRefreshRequested(string? commandType, System.Text.Json.JsonElement? parameters)
    {
        // Messenger realtime: message_sent / message_read — fast path, include sound/unread/ticks.
        if ((IsMessageSentCommand(commandType) || IsMessageReadCommand(commandType)) && _messengerViewModel != null)
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                try { await _messengerViewModel.HandleRealtimeCommandAsync(commandType, parameters); }
                catch (Exception ex) { _logger.LogWarning(ex, "messenger realtime failed ({CommandType})", commandType); }
            });
            return;
        }

        // Never drop a live update: if a refresh is already running, queue one more pass.
        if (System.Threading.Interlocked.CompareExchange(ref _realtimeRefreshBusy, 1, 0) != 0)
        {
            System.Threading.Interlocked.Exchange(ref _realtimeRefreshPending, 1);
            // Coalesce Cloud full sync even when UI refresh is busy.
            if (IsCloudDataChangedCommand(commandType) && _cloudViewModel != null)
                _ = _cloudViewModel.RequestFullSyncFromRealtimeAsync();
            return;
        }

        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                do
                {
                    System.Threading.Interlocked.Exchange(ref _realtimeRefreshPending, 0);
                    if (_messengerViewModel != null)
                        await _messengerViewModel.RefreshFromRealtimeAsync();
                    if (_cloudViewModel != null)
                    {
                        await _cloudViewModel.RefreshFromRealtimeAsync();
                        if (IsCloudDataChangedCommand(commandType))
                            await _cloudViewModel.RequestFullSyncFromRealtimeAsync();
                    }
                    _notesViewModel?.RefreshTree();
                }
                while (System.Threading.Interlocked.Exchange(ref _realtimeRefreshPending, 0) == 1);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Realtime refresh failed ({CommandType})", commandType);
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _realtimeRefreshBusy, 0);
                if (System.Threading.Interlocked.Exchange(ref _realtimeRefreshPending, 0) == 1)
                    OnRealtimeRefreshRequested("queued", null);
            }
        });
    }

    private static bool IsCloudDataChangedCommand(string? commandType) =>
        string.Equals(commandType, "data_changed", StringComparison.OrdinalIgnoreCase);

    private static bool IsMessageSentCommand(string? commandType) =>
        string.Equals(commandType, "message_sent", StringComparison.OrdinalIgnoreCase);

    private static bool IsMessageReadCommand(string? commandType) =>
        string.Equals(commandType, "message_read", StringComparison.OrdinalIgnoreCase);

    private async Task EnsureRealtimeConnectedAsync()
    {
        if (!_authService.IsAuthenticated) return;
        var settings = _settingsService.LoadSettings();
        try
        {
            await _realtime.ConnectAsync(
                settings.ApiBaseAddress,
                () => Task.FromResult(_authService.CurrentToken?.AccessToken));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR connect failed");
        }
    }

    private void ShowMessengerContent()
    {
        MessengerTabContent.Children.Clear();
        _messengerViewModel = null;
        if (!_authService.IsAuthenticated)
        {
            MessengerTabContent.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Please log in in Settings.",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });
            _ = _realtime.DisconnectAsync();
            return;
        }
        var settings = _settingsService.LoadSettings();
        var handler = CreateAuthHandler();
        handler.InnerHandler = new GetEntitiesThrottlingHandler(GetEntitiesThrottlingHandler.Lane.Interactive)
        {
            InnerHandler = WrapWithDevToolsLogging(new HttpClientHandler())
        };
        // Contacts/init share this client — keep Cloud-UI-class headroom. Chat Loading hang is bounded
        // inside LoadMessagesCoreAsync (MessengerChatLoad.InteractiveHttpTimeout), not here.
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(settings.ApiBaseAddress),
            Timeout = TimeSpan.FromSeconds(45)
        };
        var vm = new MessengerViewModel(_authService, client, _settingsService);
        vm.UnreadChanged += count => TaskbarUnreadBadge.Apply(this, count);
        vm.TryOpenEntityInAppAsync = async entityId =>
        {
            // E4 — Cloud native when navigable; switch to Cloud tab.
            MainTabControl.SelectedIndex = 2;
            if (_cloudViewModel == null) return false;
            return await _cloudViewModel.TryOpenEntityAsync(entityId);
        };
        _messengerViewModel = vm;
        MessengerTabContent.Children.Add(new MessengerView { DataContext = vm });
        _ = EnsureRealtimeConnectedAsync();
    }
}
