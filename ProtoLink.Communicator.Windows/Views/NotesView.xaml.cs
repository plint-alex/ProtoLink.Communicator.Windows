using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using ProtoLink.Communicator.Windows.Dialogs;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Notes;
using ProtoLink.Communicator.Windows.Utilities;
using ProtoLink.Communicator.Windows.ViewModels;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace ProtoLink.Communicator.Windows.Views;

public partial class NotesView : System.Windows.Controls.UserControl
{
    private const double CompactBreakpoint = 700;
    private bool _isLoading;
    private bool _isInternalSave;
    private bool _suppressExternalReloadPrompt;
    private NotesViewModel? _vm;
    private NotesIndexFileWatcher? _indexWatcher;
    private NotesNotesRootWatcher? _rootWatcher;
    private NotesExternalChangeDialog? _externalChangeDialog;
    private bool _notesWebViewReady;
    private bool _webViewInitStarted;
    private Task? _webViewInitTask;
    private readonly HashSet<string> _rememberedExpandedPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _treeEventsHooked;
    private bool _suppressTreeExpandMemory;
    /// <summary>Ignore TreeView selection changes while restoring selection after refresh.</summary>
    private bool _suppressTreeSelectionNavigate;
    private GridLength _treeColumnWidth = new(320);
    private GridLength _siblingPanelHeight = new(220);
    private const string NotesFolderDragFormat = "ProtoLink.NotesFolderPath";
    private System.Windows.Point _treeDragStartPoint;
    private TreeViewItem? _treeDragSourceItem;
    private bool _treeDragInProgress;

    public NotesView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += OnNotesVisibilityChanged;
        NotesWebView.SizeChanged += OnNotesWebViewSizeChanged;
        EditorHost.SizeChanged += OnEditorHostSizeChangedForFormatBar;
        EditorHost.SizeChanged += OnEditorHostSizeChangedForWebViewHwnd;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        HookViewModel(DataContext as NotesViewModel);
        // Always repaint: TabControl SelectedContent unloads this view when leaving the Notes tab,
        // so async TreeRefreshed can be missed while unhooked / Items can be empty on return.
        PopulateTree();
        StartRootWatcher();
        ApplyResponsiveLayout();
        ApplySiblingPanelLayout();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Do not unhook on tab unload path — only when DataContext truly changes.
        if (ReferenceEquals(e.OldValue, e.NewValue)) return;
        if (e.OldValue is NotesViewModel oldVm && _treeEventsHooked && ReferenceEquals(_vm, oldVm))
        {
            oldVm.TreeRefreshed -= PopulateTree;
            oldVm.RequestNewFolder -= OnRequestNewFolder;
            oldVm.RequestChooseNotesRoot -= OnRequestChooseNotesRoot;
            oldVm.InternalNoteWriteStarting -= OnInternalNoteWriteStarting;
            oldVm.InternalNoteWriteCompleted -= OnInternalNoteWriteCompleted;
            oldVm.NotesPagePathChanged -= OnNotesPagePathChanged;
            oldVm.NotesRootPathChanged -= OnNotesRootPathChanged;
            oldVm.ContentSaved -= OnContentSaved;
            oldVm.NavigateToNoteRequested -= OnNavigateToNoteRequested;
            oldVm.ReturnToListRequested -= OnReturnToListRequested;
            oldVm.ShareAffordanceChanged -= OnShareAffordanceChanged;
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            _treeEventsHooked = false;
            _vm = null;
        }
        HookViewModel(e.NewValue as NotesViewModel);
    }

    private void HookViewModel(NotesViewModel? vm)
    {
        _vm = vm;
        EnsureNotesEventHandlers();
    }

    private void EnsureNotesEventHandlers()
    {
        if (_vm == null || _treeEventsHooked) return;
        _vm.TreeRefreshed += PopulateTree;
        _vm.RequestNewFolder += OnRequestNewFolder;
        _vm.RequestChooseNotesRoot += OnRequestChooseNotesRoot;
        _vm.InternalNoteWriteStarting += OnInternalNoteWriteStarting;
        _vm.InternalNoteWriteCompleted += OnInternalNoteWriteCompleted;
        _vm.NotesPagePathChanged += OnNotesPagePathChanged;
        _vm.NotesRootPathChanged += OnNotesRootPathChanged;
        _vm.ContentSaved += OnContentSaved;
        _vm.NavigateToNoteRequested += OnNavigateToNoteRequested;
        _vm.ReturnToListRequested += OnReturnToListRequested;
        _vm.ShareAffordanceChanged += OnShareAffordanceChanged;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _treeEventsHooked = true;
    }

    private void OnShareAffordanceChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(OnShareAffordanceChanged);
            return;
        }

        foreach (TreeViewItem root in NotesTreeView.Items.OfType<TreeViewItem>())
            RefreshShareGlyphRecursive(root);
    }

    private void RefreshShareGlyphRecursive(TreeViewItem node)
    {
        if (node.Tag is TreeItemViewModel vm)
            ApplyTreeItemHeader(node, vm);
        foreach (var child in node.Items.OfType<TreeViewItem>())
            RefreshShareGlyphRecursive(child);
    }

    private void OnRequestChooseNotesRoot()
    {
        if (_vm == null) return;
        var path = FolderPicker.PickFolder("Select notes root folder", _vm.RootPath);
        if (path is null) return;
        _vm.SetNotesRootPath(path);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotesViewModel.IsCompactLayout)
            or nameof(NotesViewModel.ShowEditorInCompact))
            ApplyColumnLayout();
        if (e.PropertyName is nameof(NotesViewModel.HasOpenPage) or nameof(NotesViewModel.CurrentPagePath))
            HideFormatBar();
        if (e.PropertyName is nameof(NotesViewModel.ShowSiblingFiles)
            or nameof(NotesViewModel.SiblingTreeRoot)
            or nameof(NotesViewModel.SiblingFiles))
            ApplySiblingPanelLayout();
    }

    private void ApplySiblingPanelLayout()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ApplySiblingPanelLayout);
            return;
        }

        var show = _vm?.ShowSiblingFiles == true;
        // Spacer reserves layout space; Popup (own HWND) draws Folder files above WebView2 airspace.
        var vis = show ? Visibility.Visible : Visibility.Collapsed;
        SiblingPanelSpacer.Visibility = vis;
        SiblingSplitter.Visibility = vis;

        if (show)
        {
            if (SiblingPanelRow.Height.Value > 0)
                _siblingPanelHeight = SiblingPanelRow.Height;
            SiblingSplitterRow.Height = new GridLength(4);
            SiblingPanelRow.MinHeight = 120;
            SiblingPanelRow.MaxHeight = 320;
            SiblingPanelRow.Height = _siblingPanelHeight.Value > 0 ? _siblingPanelHeight : new GridLength(220);
        }
        else
        {
            if (SiblingPanelRow.Height.Value > 0)
                _siblingPanelHeight = SiblingPanelRow.Height;
            SiblingSplitterRow.Height = new GridLength(0);
            SiblingPanelRow.MinHeight = 0;
            SiblingPanelRow.Height = new GridLength(0);
        }

        EditorPane.UpdateLayout();
        EditorHost.UpdateLayout();
        ForceWebView2LayoutSync();

        // Popup does not inherit DataContext from the visual tree — assign explicitly.
        SiblingFilesPopup.DataContext = _vm;
        SiblingFilesPopup.IsOpen = show;
        if (show)
        {
            SiblingPanel.Width = Math.Max(1, SiblingPanelSpacer.ActualWidth);
            SiblingPanel.Height = Math.Max(1, SiblingPanelSpacer.ActualHeight);
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (show && SiblingPanelSpacer.ActualWidth > 1)
            {
                SiblingPanel.Width = SiblingPanelSpacer.ActualWidth;
                SiblingPanel.Height = Math.Max(1, SiblingPanelSpacer.ActualHeight);
            }
        }, DispatcherPriority.Loaded);
    }

    private void OnEditorHostSizeChangedForWebViewHwnd(object sender, SizeChangedEventArgs e) =>
        SyncWebView2ToEditorHost();

    /// <summary>
    /// WebView2 HWND ignores some WPF Grid row shrinks and paints over Folder files.
    /// Keep explicit Width/Height equal to EditorHost so the interop surface matches the editor row.
    /// </summary>
    private void SyncWebView2ToEditorHost()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(SyncWebView2ToEditorHost);
            return;
        }

        var hostH = EditorHost.ActualHeight;
        var hostW = EditorHost.ActualWidth;
        if (hostH <= 1 || hostW <= 1) return;

        NotesWebView.Width = hostW;
        NotesWebView.Height = hostH;
    }

    private void ForceWebView2LayoutSync() => SyncWebView2ToEditorHost();

    private void OnNavigateToNoteRequested(string path) =>
        _ = NavigateToAsync(path, forceReload: false, fromHistory: true);

    private void OnReturnToListRequested() => ApplyColumnLayout();

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.PreviousSize.Width - e.NewSize.Width) < 0.5) return;
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        if (_vm == null) return;
        _vm.IsCompactLayout = ActualWidth > 0 && ActualWidth < CompactBreakpoint;
        ApplyColumnLayout();
    }

    private void ApplyColumnLayout()
    {
        if (_vm == null) return;

        if (_vm.IsCompactLayout)
        {
            if (TreeColumn.Width.Value > 0)
                _treeColumnWidth = TreeColumn.Width;

            PaneSplitter.Visibility = Visibility.Collapsed;
            SplitterColumn.Width = new GridLength(0);

            var showEditor = _vm.ShowEditorInCompact && _vm.HasOpenPage;
            if (showEditor)
            {
                TreeColumn.Width = new GridLength(0);
                TreeColumn.MinWidth = 0;
                EditorColumn.Width = new GridLength(1, GridUnitType.Star);
                TreePane.Visibility = Visibility.Collapsed;
                EditorPane.Visibility = Visibility.Visible;
            }
            else
            {
                TreeColumn.MinWidth = 0;
                TreeColumn.MaxWidth = double.PositiveInfinity;
                TreeColumn.Width = new GridLength(1, GridUnitType.Star);
                EditorColumn.Width = new GridLength(0);
                TreePane.Visibility = Visibility.Visible;
                EditorPane.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            TreeColumn.MinWidth = 280;
            TreeColumn.MaxWidth = 420;
            TreeColumn.Width = _treeColumnWidth.Value > 0 ? _treeColumnWidth : new GridLength(320);
            SplitterColumn.Width = new GridLength(4);
            EditorColumn.Width = new GridLength(1, GridUnitType.Star);
            PaneSplitter.Visibility = Visibility.Visible;
            TreePane.Visibility = Visibility.Visible;
            EditorPane.Visibility = Visibility.Visible;
        }
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_vm == null) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            if (_vm.BackCommand.CanExecute(null))
                _vm.BackCommand.Execute(null);
            e.Handled = true;
        }
        else if (key == Key.Escape)
        {
            if (FormatBarPopup.IsOpen)
            {
                HideFormatBar();
                e.Handled = true;
            }
            else if (_vm.IsCompactLayout && _vm.ShowEditorInCompact)
            {
                _vm.ReturnToListInCompact();
                e.Handled = true;
            }
        }
        else if (key == Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (_vm.CopyLinkCommand.CanExecute(null))
                _vm.CopyLinkCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.XButton1 || _vm == null) return;
        if (_vm.BackCommand.CanExecute(null))
            _vm.BackCommand.Execute(null);
        e.Handled = true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopIndexWatcher();
        StopRootWatcher();
        SiblingFilesPopup.IsOpen = false;
        // Keep TreeRefreshed (and other VM) handlers subscribed across tab switches.
        // TabControl SelectedContent unloads this view when Notes is not selected; unhooking
        // here drops async RefreshTree completions and left users with an empty tree.
    }

    private void OnNotesVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        TryStartWebViewInit();

    private void OnNotesWebViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
            TryStartWebViewInit();
    }

    /// <summary>Call when the Notes tab becomes selected (WebView2 needs a visible HWND).</summary>
    public void ActivateNotesTab()
    {
        EnsureNotesEventHandlers();
        if (NotesTreeView.Items.Count == 0)
        {
            if (_vm?.TreeRoot != null)
                PopulateTree();
            else
                _vm?.RefreshTree();
        }
        TryStartWebViewInit();
        _ = Dispatcher.InvokeAsync(ReloadOpenPageFromDiskIfCleanAsync);
    }

    /// <summary>Release WebView2 / watchers before process exit so native unload is orderly.</summary>
    public void ShutdownForAppExit()
    {
        StopIndexWatcher();
        StopRootWatcher();
        _notesWebViewReady = false;
        _webViewInitStarted = false;
        try
        {
            NotesWebView.Dispose();
        }
        catch
        {
            // Best-effort: browser process may already be gone during shutdown.
        }
    }

    /// <summary>Refresh open note from disk when tab is shown and editor is clean.</summary>
    private async Task ReloadOpenPageFromDiskIfCleanAsync()
    {
        if (_isInternalSave || _isLoading || _vm?.CurrentPagePath == null) return;
        if (_vm.HasUnsavedWork) return;
        var core = NotesWebView.CoreWebView2;
        if (core == null) return;
        try
        {
            var diskInner = await _vm.ReadDiskInnerHtmlAsync(_vm.CurrentPagePath);
            if (string.Equals(diskInner, _vm.HtmlSyncedToDisk, StringComparison.Ordinal))
                return;

            var keepFocus = NotesWebView.IsKeyboardFocusWithin;
            _isLoading = true;
            var html = await _vm.LoadPageContentAsync(_vm.CurrentPagePath);
            core.NavigateToString(html);
            _vm.StatusText = "Reloaded";
            if (keepFocus)
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => NotesWebView.Focus()));
        }
        catch
        {
            // Leave current buffer; disk watcher / next activate can retry.
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void TryStartWebViewInit()
    {
        if (_notesWebViewReady || _webViewInitStarted) return;
        if (!IsLoaded || !IsVisible || !IsNotesTabVisible()) return;
        if (!NotesWebView.IsVisible || NotesWebView.ActualWidth < 2 || NotesWebView.ActualHeight < 2)
            return;
        _webViewInitStarted = true;
        _webViewInitTask = InitWebViewAsync();
    }

    private bool IsNotesTabVisible()
    {
        for (var el = (DependencyObject?)this; el != null; el = VisualTreeHelper.GetParent(el))
        {
            if (el is UIElement ui && ui.Visibility != Visibility.Visible)
                return false;
        }
        return true;
    }

    private void OnNotesRootPathChanged()
    {
        if (Dispatcher.CheckAccess())
            RestartRootWatcher();
        else
            Dispatcher.BeginInvoke(new Action(RestartRootWatcher));
    }

    private void RestartRootWatcher()
    {
        StopRootWatcher();
        StartRootWatcher();
    }

    private void OnContentSaved(string _) => _suppressExternalReloadPrompt = false;

    private void StartRootWatcher()
    {
        if (_vm == null) return;
        var root = _vm.RootPath;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
        try
        {
            StopRootWatcher();
            _rootWatcher = new NotesNotesRootWatcher(root);
            _rootWatcher.TreeStructureChanged += OnNotesRootStructureChanged;
        }
        catch
        {
            // Ignore watcher failures during path transitions.
        }
    }

    private void StopRootWatcher()
    {
        if (_rootWatcher == null) return;
        _rootWatcher.TreeStructureChanged -= OnNotesRootStructureChanged;
        _rootWatcher.Dispose();
        _rootWatcher = null;
    }

    private void OnNotesRootStructureChanged()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_isInternalSave) return;
            _vm?.RefreshTree();
            _vm?.RefreshSiblingFiles();
        }));
    }

    private void OnSiblingTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null) return;
        if (SiblingFilesTreeView.SelectedItem is not TreeItemViewModel item) return;
        if (item.Item.Type != FileSystemItemType.File) return;
        _vm.OpenSiblingPath(item.Item.FullPath);
        e.Handled = true;
    }

    private void OnSiblingTreeContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var vm = FindSiblingTreeItem(e.OriginalSource as DependencyObject);
        SiblingTreeContextMenu.Tag = vm;
        var isFile = vm?.Item.Type == FileSystemItemType.File;
        SiblingOpenMenuItem.IsEnabled = isFile;
        SiblingOpenInExplorerMenuItem.IsEnabled = vm != null;
        if (vm == null)
            e.Handled = true;
    }

    private void OnSiblingOpenClick(object sender, RoutedEventArgs e)
    {
        if (_vm == null || SiblingTreeContextMenu.Tag is not TreeItemViewModel vm) return;
        SiblingTreeContextMenu.Tag = null;
        if (vm.Item.Type == FileSystemItemType.File)
            _vm.OpenSiblingPath(vm.Item.FullPath);
    }

    private void OnSiblingOpenInExplorerClick(object sender, RoutedEventArgs e)
    {
        if (_vm == null || SiblingTreeContextMenu.Tag is not TreeItemViewModel vm) return;
        SiblingTreeContextMenu.Tag = null;
        var path = vm.Item.FullPath;
        if (vm.Item.Type == FileSystemItemType.Folder)
        {
            if (!WindowsExplorer.TryOpenFolderSelected(path))
                _vm.StatusText = "Folder not found";
            return;
        }

        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            _vm.StatusText = "Folder not found";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = WindowsExplorer.BuildSelectArguments(path),
                UseShellExecute = true
            });
        }
        catch
        {
            _vm.StatusText = "Could not open Explorer";
        }
    }

    private static TreeItemViewModel? FindSiblingTreeItem(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is TreeViewItem { DataContext: TreeItemViewModel vm })
                return vm;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void OnNotesPagePathChanged(string newFolderPath)
    {
        if (Dispatcher.CheckAccess())
        {
            AttachIndexWatcher(newFolderPath);
            if (_vm != null)
            {
                _vm.RecordOpenedPage(null, newFolderPath, pushHistory: false);
                if (_vm.IsCompactLayout && !string.IsNullOrEmpty(newFolderPath))
                {
                    _vm.ShowEditorInCompact = true;
                    ApplyColumnLayout();
                }
            }
        }
        else
            Dispatcher.BeginInvoke(new Action(() => OnNotesPagePathChanged(newFolderPath)));
    }

    private void AttachIndexWatcher(string folderPath)
    {
        if (_vm == null) return;
        StopIndexWatcher();
        var indexPath = _vm.GetWatchedIndexPath(folderPath);
        _indexWatcher = new NotesIndexFileWatcher(indexPath);
        _indexWatcher.Changed += OnIndexFileChanged;
        _indexWatcher.IndexFileDeleted += OnIndexFileDeleted;
    }

    private void OnInternalNoteWriteStarting()
    {
        if (Dispatcher.CheckAccess())
            _isInternalSave = true;
        else
            Dispatcher.BeginInvoke(new Action(() => _isInternalSave = true));
    }

    private void OnInternalNoteWriteCompleted()
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(1000);
            _isInternalSave = false;
        });
    }

    private static string WebView2UserDataFolder()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProtoLinkCommunicator",
            "WebView2-Notes");
        Directory.CreateDirectory(path);
        return path;
    }

    private async Task InitWebViewAsync()
    {
        try
        {
            // Let layout finish; creating the controller with a zero-size / hidden HWND
            // frequently throws COMException 0x8000FFFF (E_UNEXPECTED) on Windows.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(50);

            if (!IsNotesTabVisible())
            {
                _webViewInitStarted = false;
                return;
            }

            if (NotesWebView.CoreWebView2 != null)
            {
                WireWebViewEvents();
                _notesWebViewReady = true;
                return;
            }

            var userData = WebView2UserDataFolder();
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            try
            {
                await EnsureCoreWebView2WithRetryAsync(env);
            }
            catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == 0x8000FFFF)
            {
                // Corrupted profile or stale lock — recreate folder once and retry.
                TryClearWebView2Profile(userData);
                env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
                await EnsureCoreWebView2WithRetryAsync(env);
            }

            WireWebViewEvents();
            _notesWebViewReady = true;
            if (_vm != null)
                _vm.StatusText = "Notes editor ready";

            if (_vm?.CurrentPagePath != null && Directory.Exists(_vm.CurrentPagePath))
                await NavigateToAsync(_vm.CurrentPagePath);
        }
        catch (Exception ex)
        {
            _webViewInitStarted = false;
            if (_vm != null) _vm.StatusText = "WebView error: " + ex.Message;
            ErrorDetailDialog.Show("WebView", ex);
        }
    }

    private static void TryClearWebView2Profile(string userDataFolder)
    {
        try
        {
            if (Directory.Exists(userDataFolder))
                Directory.Delete(userDataFolder, recursive: true);
            Directory.CreateDirectory(userDataFolder);
        }
        catch
        {
            // Best effort; environment create may still succeed.
        }
    }

    private async Task EnsureCoreWebView2WithRetryAsync(CoreWebView2Environment env)
    {
        try
        {
            await NotesWebView.EnsureCoreWebView2Async(env);
        }
        catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == 0x8000FFFF)
        {
            // Controllers occasionally fail on first create after tab switch / profile lock.
            await Task.Delay(250);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await NotesWebView.EnsureCoreWebView2Async(env);
        }
    }

    private void WireWebViewEvents()
    {
        var core = NotesWebView.CoreWebView2;
        if (core == null) return;
        core.WebMessageReceived -= OnWebMessageReceived;
        core.NavigationStarting -= OnWebViewNavigationStarting;
        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationStarting += OnWebViewNavigationStarting;
    }

    private static void OnWebViewNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var uri = e.Uri;
        if (string.IsNullOrEmpty(uri)) return;
        if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            e.Cancel = true;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var json = e.TryGetWebMessageAsString();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var typeEl))
            return;
        var type = typeEl.GetString();
        if (type is "selectionChanged" or "dismissFormatBar")
        {
            HandleFormatBarWebMessage(root, type);
            return;
        }

        if (type == "contentChanged")
        {
            Dispatcher.BeginInvoke(new Action(() => _ = OnContentChangedFromEditorAsync()));
            return;
        }

        if (type != "openLink" || !root.TryGetProperty("url", out var urlEl))
            return;
        var url = urlEl.GetString();
        if (string.IsNullOrEmpty(url))
            return;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            void Show()
            {
                System.Windows.MessageBox.Show($"Could not open link: {ex.Message}", "Open link",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            if (Dispatcher.CheckAccess())
                Show();
            else
                Dispatcher.BeginInvoke(new Action(Show));
        }
    }

    private async Task OnContentChangedFromEditorAsync()
    {
        try
        {
            if (_isLoading || _vm?.CurrentPagePath == null) return;
            var html = await GetHtmlFromEditorAsync();
            await _vm.HandleEditorContentChangedAsync(html);
        }
        catch (Exception ex)
        {
            ErrorDetailDialog.Show("Notes editor", ex);
        }
    }

    private async Task<string> GetHtmlFromEditorAsync()
    {
        var core = NotesWebView.CoreWebView2 ?? throw new InvalidOperationException("WebView is not ready.");
        var result = await core.ExecuteScriptAsync("window.getHtml();");
        return JsonSerializer.Deserialize<string>(result)
               ?? throw new JsonException("Editor returned no HTML.");
    }

    private void PopulateTree()
    {
        if (_vm?.TreeRoot == null)
        {
            NotesTreeView.Items.Clear();
            return;
        }

        var rootPath = _vm.TreeRoot.Item.FullPath;
        var keepEditorFocus = NotesWebView.IsKeyboardFocusWithin;
        // Merge/rebuild removes live TreeViewItems; WPF then selects a parent (often root) and
        // SelectedItemChanged would NavigateToAsync that parent — keep CurrentPagePath/editor stable.
        var preservePagePath = _vm.CurrentPagePath;

        if (NotesTreeView.Items.Count == 1
            && NotesTreeView.Items[0] is TreeViewItem existingRoot
            && existingRoot.Tag is TreeItemViewModel existingVm
            && string.Equals(
                NormalizePathKey(existingVm.Item.FullPath),
                NormalizePathKey(rootPath),
                StringComparison.OrdinalIgnoreCase))
        {
            _suppressTreeSelectionNavigate = true;
            try
            {
                // In-place merge: never rewrite IsExpanded — live TreeViewItems keep user collapse/expand.
                MergeTreeItem(existingRoot, _vm.TreeRoot);
                TryReselectCurrentPageCore(focusTree: false);
                EnsurePreservedPagePath(preservePagePath);
            }
            finally
            {
                _suppressTreeSelectionNavigate = false;
            }

            if (keepEditorFocus)
                NotesWebView.Focus();
            return;
        }

        // First paint or root path changed — rebuild once; respect remembered expansion.
        var live = CollectExpandedPaths();
        if (live.Count > 0)
        {
            _rememberedExpandedPaths.Clear();
            foreach (var p in live)
                _rememberedExpandedPaths.Add(p);
        }
        else if (_rememberedExpandedPaths.Count == 0)
        {
            // First load only: expand root so the tree is usable.
            _rememberedExpandedPaths.Add(NormalizePathKey(rootPath));
        }

        // Build before Clear so a create failure cannot leave an empty tree.
        var rootItem = CreateTreeItem(_vm.TreeRoot, _rememberedExpandedPaths);
        _suppressTreeSelectionNavigate = true;
        try
        {
            NotesTreeView.Items.Clear();
            NotesTreeView.Items.Add(rootItem);
        }
        catch
        {
            _suppressTreeSelectionNavigate = false;
            throw;
        }

        // Keep suppress until after reselect — Clear/Add can select root before Loaded.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            try
            {
                TryReselectCurrentPageCore(focusTree: false);
                EnsurePreservedPagePath(preservePagePath);
                if (keepEditorFocus)
                    NotesWebView.Focus();
            }
            finally
            {
                _suppressTreeSelectionNavigate = false;
            }
        }));
    }

    /// <summary>
    /// If a tree mutation somehow changed <see cref="NotesViewModel.CurrentPagePath"/>, restore it
    /// when the previous page still exists (deleted pages intentionally clear elsewhere).
    /// </summary>
    private void EnsurePreservedPagePath(string? preservePagePath)
    {
        if (_vm == null || string.IsNullOrEmpty(preservePagePath)) return;
        if (!Directory.Exists(preservePagePath)) return;
        if (string.Equals(_vm.CurrentPagePath, preservePagePath, StringComparison.OrdinalIgnoreCase))
            return;
        _vm.CurrentPagePath = preservePagePath;
    }

    /// <summary>Patch an existing TreeViewItem from model data without rewriting IsExpanded except via restore after replace.</summary>
    private void MergeTreeItem(TreeViewItem tvi, TreeItemViewModel vm)
    {
        tvi.Tag = vm;
        ApplyTreeItemHeader(tvi, vm);

        // Index live children by normalized path; drop UI duplicates immediately.
        var byPath = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in tvi.Items.OfType<TreeViewItem>().ToList())
        {
            if (child.Tag is not TreeItemViewModel childVm)
            {
                tvi.Items.Remove(child);
                continue;
            }

            var key = NormalizePathKey(childVm.Item.FullPath);
            if (string.IsNullOrEmpty(key) || !byPath.TryAdd(key, child))
                tvi.Items.Remove(child);
        }

        // Model order, first path wins (skip duplicate model entries).
        var ordered = new List<TreeViewItem>(vm.Children.Count);
        var seenWant = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var childVm in vm.Children)
        {
            var key = NormalizePathKey(childVm.Item.FullPath);
            if (string.IsNullOrEmpty(key) || !seenWant.Add(key))
                continue;

            if (byPath.Remove(key, out var existing))
            {
                MergeTreeItem(existing, childVm);
                ordered.Add(existing);
            }
            else
            {
                ordered.Add(CreateTreeItem(childVm, _rememberedExpandedPaths));
            }
        }

        // Orphans (not in model) are dropped by the replace below — no need to Remove individually.

        _suppressTreeExpandMemory = true;
        try
        {
            var expandState = new bool[ordered.Count];
            for (var i = 0; i < ordered.Count; i++)
                expandState[i] = ordered[i].IsExpanded;

            tvi.Items.Clear();
            for (var i = 0; i < ordered.Count; i++)
            {
                tvi.Items.Add(ordered[i]);
                ordered[i].IsExpanded = expandState[i];
            }
        }
        finally
        {
            _suppressTreeExpandMemory = false;
        }
    }

    private static string NormalizePathKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    /// <summary>
    /// Apply remembered expansion onto the live tree (new-page UX only).
    /// Does not collapse anything — only expands paths in the set.
    /// </summary>
    private void ApplyRememberedExpansionToLiveTree()
    {
        foreach (TreeViewItem tvi in NotesTreeView.Items.OfType<TreeViewItem>())
            ApplyRememberedExpansionRecursive(tvi);
    }

    private void ApplyRememberedExpansionRecursive(TreeViewItem node)
    {
        if (node.Tag is TreeItemViewModel vm)
        {
            var key = NormalizePathKey(vm.Item.FullPath);
            if ((!string.IsNullOrEmpty(key) && _rememberedExpandedPaths.Contains(key))
                || _rememberedExpandedPaths.Contains(vm.Item.FullPath))
                node.IsExpanded = true;
        }
        foreach (TreeViewItem child in node.Items.OfType<TreeViewItem>())
            ApplyRememberedExpansionRecursive(child);
    }

    private static void UpdateTreeItemHeader(TreeViewItem tvi, string name)
    {
        if (tvi.Header is TextBlock tb)
        {
            if (!string.Equals(tb.Text, name, StringComparison.Ordinal))
                tb.Text = name;
            return;
        }

        if (tvi.Header is StackPanel panel
            && panel.Children.OfType<TextBlock>().FirstOrDefault() is { } title)
        {
            if (!string.Equals(title.Text, name, StringComparison.Ordinal))
                title.Text = name;
            return;
        }

        if (tvi.Header is string s)
        {
            if (!string.Equals(s, name, StringComparison.Ordinal))
                tvi.Header = name;
            return;
        }

        // Legacy DockPanel+icon headers from older sessions — replace with name-only.
        tvi.Header = new TextBlock
        {
            Text = name,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
    }

    private void EnsureAncestorsExpanded(string? pagePath)
    {
        if (string.IsNullOrEmpty(pagePath) || string.IsNullOrEmpty(_vm?.RootPath)) return;
        try
        {
            var root = NormalizePathKey(_vm.RootPath);
            var current = NormalizePathKey(pagePath);
            while (!string.IsNullOrEmpty(current))
            {
                _rememberedExpandedPaths.Add(current);
                if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
                    break;
                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) ||
                    string.Equals(NormalizePathKey(parent), current, StringComparison.OrdinalIgnoreCase))
                    break;
                current = NormalizePathKey(parent);
            }
        }
        catch
        {
            // Ignore path normalization failures.
        }
    }

    private HashSet<string> CollectExpandedPaths()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TreeViewItem tvi in NotesTreeView.Items.OfType<TreeViewItem>())
            CollectExpandedRecursive(tvi, set);
        return set;
    }

    private static void CollectExpandedRecursive(TreeViewItem node, HashSet<string> set)
    {
        if (node.Tag is TreeItemViewModel vm && node.IsExpanded)
            set.Add(NormalizePathKey(vm.Item.FullPath));
        foreach (TreeViewItem child in node.Items.OfType<TreeViewItem>())
            CollectExpandedRecursive(child, set);
    }

    private TreeViewItem CreateTreeItem(TreeItemViewModel vm, IReadOnlySet<string>? expandedPaths = null)
    {
        var item = new TreeViewItem { Tag = vm };
        ApplyTreeItemHeader(item, vm);
        if (expandedPaths != null)
        {
            var pathKey = NormalizePathKey(vm.Item.FullPath);
            if (!string.IsNullOrEmpty(pathKey) && expandedPaths.Contains(pathKey))
                item.IsExpanded = true;
            else if (expandedPaths.Contains(vm.Item.FullPath))
                item.IsExpanded = true;
        }
        foreach (var child in vm.Children)
            item.Items.Add(CreateTreeItem(child, expandedPaths));
        return item;
    }

    private void ApplyTreeItemHeader(TreeViewItem tvi, TreeItemViewModel vm)
    {
        var title = new TextBlock
        {
            Text = vm.Name,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var showShared = _vm?.ShowsExternalShareAffordance(vm.Item.FullPath) == true;
        if (!showShared)
        {
            tvi.Header = title;
            return;
        }

        var people = new TextBlock
        {
            Text = "\uE716",
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 12,
            Opacity = 0.55,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TryFindResource("ForegroundMutedBrush") as System.Windows.Media.Brush,
            ToolTip = ShareSheetStrings.ShareMenu(_vm?.ShareSheet?.Lang)
        };
        System.Windows.Automation.AutomationProperties.SetName(people, "Shared");

        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        row.Children.Add(title);
        row.Children.Add(people);
        tvi.Header = row;
    }

    private void TryReselectCurrentPage(bool focusTree = false)
    {
        _suppressTreeSelectionNavigate = true;
        try
        {
            TryReselectCurrentPageCore(focusTree);
        }
        finally
        {
            _suppressTreeSelectionNavigate = false;
        }
    }

    /// <summary>Select CurrentPagePath in the live tree. Caller owns <see cref="_suppressTreeSelectionNavigate"/> when nesting inside PopulateTree.</summary>
    private void TryReselectCurrentPageCore(bool focusTree = false)
    {
        var path = _vm?.CurrentPagePath;
        if (string.IsNullOrEmpty(path)) return;

        // Keep the open page visible after refresh: expand ancestors, then select.
        EnsureAncestorsExpanded(path);
        ExpandRememberedAncestorsForPath(path);

        var targetKey = NormalizePathKey(path);
        if (string.IsNullOrEmpty(targetKey)) return;

        foreach (TreeViewItem tvi in NotesTreeView.Items.OfType<TreeViewItem>())
        {
            if (TrySelectUnder(tvi, targetKey, focusTree))
                return;
        }
    }

    /// <summary>Expand live tree nodes on the path to <paramref name="pagePath"/> so selection is visible.</summary>
    private void ExpandRememberedAncestorsForPath(string pagePath)
    {
        var targetKey = NormalizePathKey(pagePath);
        if (string.IsNullOrEmpty(targetKey)) return;

        _suppressTreeExpandMemory = true;
        try
        {
            foreach (TreeViewItem root in NotesTreeView.Items.OfType<TreeViewItem>())
                ExpandAlongPath(root, targetKey);
        }
        finally
        {
            _suppressTreeExpandMemory = false;
        }
    }

    private static bool ExpandAlongPath(TreeViewItem node, string targetKey)
    {
        if (node.Tag is not TreeItemViewModel vm) return false;
        var nodeKey = NormalizePathKey(vm.Item.FullPath);
        if (string.IsNullOrEmpty(nodeKey)) return false;

        if (string.Equals(nodeKey, targetKey, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!PathKeyIsUnderOrEqual(nodeKey, targetKey))
            return false;

        node.IsExpanded = true;
        foreach (TreeViewItem child in node.Items.OfType<TreeViewItem>())
        {
            if (ExpandAlongPath(child, targetKey))
                return true;
        }
        return false;
    }

    private static bool PathKeyIsUnderOrEqual(string nodeKey, string targetKey)
    {
        if (string.Equals(nodeKey, targetKey, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = nodeKey + Path.DirectorySeparatorChar;
        return targetKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrySelectUnder(TreeViewItem node, string targetKey, bool focusTree)
    {
        if (node.Tag is not TreeItemViewModel vm) return false;
        var nodeKey = NormalizePathKey(vm.Item.FullPath);
        if (string.IsNullOrEmpty(nodeKey)) return false;

        if (string.Equals(nodeKey, targetKey, StringComparison.OrdinalIgnoreCase))
        {
            node.IsSelected = true;
            node.BringIntoView();
            if (focusTree)
                node.Focus();
            return true;
        }

        if (!PathKeyIsUnderOrEqual(nodeKey, targetKey)) return false;

        foreach (TreeViewItem child in node.Items.OfType<TreeViewItem>())
        {
            if (TrySelectUnder(child, targetKey, focusTree))
                return true;
        }
        return false;
    }

    private void OnTreeItemExpanded(object sender, RoutedEventArgs e)
    {
        if (_suppressTreeExpandMemory) return;
        if (e.OriginalSource is TreeViewItem tvi && tvi.Tag is TreeItemViewModel vm)
        {
            var key = NormalizePathKey(vm.Item.FullPath);
            if (!string.IsNullOrEmpty(key))
                _rememberedExpandedPaths.Add(key);
        }
    }

    private void OnTreeItemCollapsed(object sender, RoutedEventArgs e)
    {
        if (_suppressTreeExpandMemory) return;
        if (e.OriginalSource is TreeViewItem tvi && tvi.Tag is TreeItemViewModel vm)
        {
            var key = NormalizePathKey(vm.Item.FullPath);
            if (!string.IsNullOrEmpty(key))
                _rememberedExpandedPaths.Remove(key);
            _rememberedExpandedPaths.Remove(vm.Item.FullPath);
        }
    }

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressTreeSelectionNavigate) return;
        if (e.NewValue is TreeViewItem tvi && tvi.Tag is TreeItemViewModel vm)
        {
            if (_vm?.IsCompactLayout == true)
                _vm.ShowEditorInCompact = true;
            _ = NavigateToAsync(vm.Item.FullPath);
            ApplyColumnLayout();
        }
    }

    private void OnTreePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        HideFormatBar();
        _treeDragStartPoint = e.GetPosition(null);
        _treeDragSourceItem = FindAncestorTreeViewItem(e.OriginalSource as DependencyObject);
    }

    private void OnTreePreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_treeDragInProgress || _vm == null) return;
        if (e.LeftButton != MouseButtonState.Pressed || _treeDragSourceItem == null) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _treeDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _treeDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (_treeDragSourceItem.Tag is not TreeItemViewModel vm) return;
        var sourcePath = vm.Item.FullPath;
        if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(_vm.RootPath)) return;

        try
        {
            if (string.Equals(
                    Path.GetFullPath(sourcePath),
                    Path.GetFullPath(_vm.RootPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                _treeDragSourceItem = null;
                return;
            }
        }
        catch
        {
            _treeDragSourceItem = null;
            return;
        }

        _treeDragInProgress = true;
        try
        {
            var data = new System.Windows.DataObject(NotesFolderDragFormat, sourcePath);
            DragDrop.DoDragDrop(_treeDragSourceItem, data, System.Windows.DragDropEffects.Move);
        }
        finally
        {
            _treeDragInProgress = false;
            _treeDragSourceItem = null;
        }
    }

    private void OnTreeDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
        if (_vm == null || !e.Data.GetDataPresent(NotesFolderDragFormat)) return;
        if (e.Data.GetData(NotesFolderDragFormat) is not string sourcePath) return;

        var targetItem = FindAncestorTreeViewItem(e.OriginalSource as DependencyObject);
        if (targetItem?.Tag is not TreeItemViewModel targetVm) return;

        var error = NotesFileSystemService.ValidateMove(sourcePath, targetVm.Item.FullPath, _vm.RootPath ?? "");
        if (error == null)
            e.Effects = System.Windows.DragDropEffects.Move;
    }

    private void OnTreeDrop(object sender, System.Windows.DragEventArgs e)
    {
        e.Handled = true;
        if (_vm == null || !e.Data.GetDataPresent(NotesFolderDragFormat)) return;
        if (e.Data.GetData(NotesFolderDragFormat) is not string sourcePath) return;

        var targetItem = FindAncestorTreeViewItem(e.OriginalSource as DependencyObject);
        if (targetItem?.Tag is not TreeItemViewModel targetVm) return;

        var targetPath = targetVm.Item.FullPath;
        if (!_vm.MoveFolder(sourcePath, targetPath))
            return;

        if (!string.IsNullOrEmpty(_vm.CurrentPagePath))
            _ = NavigateToAsync(_vm.CurrentPagePath, forceReload: true);
    }

    private static TreeViewItem? FindAncestorTreeViewItem(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is TreeViewItem tvi)
                return tvi;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (NotesTreeView.SelectedItem is TreeViewItem tvi)
            tvi.IsExpanded = !tvi.IsExpanded;
    }

    private void StopIndexWatcher()
    {
        if (_indexWatcher == null) return;
        _indexWatcher.Changed -= OnIndexFileChanged;
        _indexWatcher.IndexFileDeleted -= OnIndexFileDeleted;
        _indexWatcher.Dispose();
        _indexWatcher = null;
    }

    private void OnIndexFileDeleted() => _ = Dispatcher.InvokeAsync(OnIndexFileDeletedAsync);

    private async Task OnIndexFileDeletedAsync()
    {
        if (_vm == null) return;
        if (_isInternalSave) return;
        var path = _vm.CurrentPagePath;
        if (string.IsNullOrEmpty(path)) return;
        var indexPath = _vm.GetWatchedIndexPath(path);
        if (Directory.Exists(path) && File.Exists(indexPath))
            return;

        if (_vm.HasUnsavedWork)
        {
            await _vm.RestoreDeletedPageFromUnsavedAsync();
            var core = NotesWebView.CoreWebView2;
            if (core != null && _vm.CurrentPagePath != null)
            {
                var html = await _vm.LoadPageContentAsync(_vm.CurrentPagePath);
                core.NavigateToString(html);
            }
            StopIndexWatcher();
            if (_vm.CurrentPagePath != null)
                AttachIndexWatcher(_vm.CurrentPagePath);
        }
        else
        {
            _vm.CancelPendingSave();
            _vm.CurrentPagePath = null;
            StopIndexWatcher();
            NotesWebView.CoreWebView2?.NavigateToString("about:blank");
            _vm.RefreshTree();
        }
    }

    private async Task NavigateToAsync(string folderPath, bool forceReload = false, bool fromHistory = false)
    {
        if (_vm == null || !Directory.Exists(folderPath)) return;

        var samePage = string.Equals(_vm.CurrentPagePath, folderPath, StringComparison.OrdinalIgnoreCase);
        if (samePage && !forceReload && NotesWebView.CoreWebView2 != null)
        {
            AttachIndexWatcher(folderPath);
            // Re-list + layout: sibling files may appear after open; same-page must not skip E5 panel.
            _vm.RefreshSiblingFiles();
            ApplySiblingPanelLayout();
            return;
        }

        StopIndexWatcher();
        _vm.CancelPendingSave();
        _suppressExternalReloadPrompt = false;
        _isLoading = true;
        try
        {
            var previous = _vm.CurrentPagePath;
            _vm.CurrentPagePath = folderPath;
            _vm.RecordOpenedPage(previous, folderPath, pushHistory: !fromHistory);
            _vm.StatusText = "Loading…";
            var html = await _vm.LoadPageContentAsync(folderPath);
            if (NotesWebView.CoreWebView2 != null)
                NotesWebView.CoreWebView2.NavigateToString(html);
            _vm.StatusText = "Ready";

            AttachIndexWatcher(folderPath);
            // Ensure Folder files row expands after editor load (binding/layout race on first open).
            _vm.RefreshSiblingFiles();
            ApplySiblingPanelLayout();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void OnIndexFileChanged()
    {
        _ = Dispatcher.InvokeAsync(OnIndexFileChangedAsync);
    }

    private async Task OnIndexFileChangedAsync()
    {
        if (_isInternalSave || _isLoading || _vm?.CurrentPagePath == null)
            return;
        if (_suppressExternalReloadPrompt)
            return;
        var core = NotesWebView.CoreWebView2;
        if (core == null) return;

        if (!_vm.HasUnsavedWork)
        {
            try
            {
                var diskInner = await _vm.ReadDiskInnerHtmlAsync(_vm.CurrentPagePath);
                if (string.Equals(diskInner, _vm.HtmlSyncedToDisk, StringComparison.Ordinal))
                    return;
            }
            catch
            {
                // Fall through to reload attempt.
            }

            var keepFocus = NotesWebView.IsKeyboardFocusWithin;
            _isLoading = true;
            try
            {
                var html = await _vm.LoadPageContentAsync(_vm.CurrentPagePath);
                core.NavigateToString(html);
                _vm.StatusText = "Reloaded";
                if (keepFocus)
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => NotesWebView.Focus()));
            }
            finally
            {
                _isLoading = false;
            }
            return;
        }

        ShowExternalChangeConflict();
    }

    private void ShowExternalChangeConflict()
    {
        if (_externalChangeDialog != null)
        {
            try
            {
                if (_externalChangeDialog.IsLoaded)
                    return;
            }
            catch
            {
                // Dialog may already be closing
            }
        }

        var dlg = new NotesExternalChangeDialog
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        dlg.ReloadFromDiskRequested += OnExternalReloadRequested;
        dlg.KeepEditsRequested += OnExternalKeepRequested;
        dlg.Closed += (_, _) =>
        {
            dlg.ReloadFromDiskRequested -= OnExternalReloadRequested;
            dlg.KeepEditsRequested -= OnExternalKeepRequested;
            if (ReferenceEquals(_externalChangeDialog, dlg))
                _externalChangeDialog = null;
        };
        _externalChangeDialog = dlg;
        dlg.Show();
    }

    private void OnExternalReloadRequested(object? sender, EventArgs e) =>
        _ = Dispatcher.InvokeAsync(ExecuteExternalReloadAsync);

    private async Task ExecuteExternalReloadAsync()
    {
        if (_vm?.CurrentPagePath == null) return;
        var core = NotesWebView.CoreWebView2;
        if (core == null) return;
        _vm.CancelPendingSave();
        _suppressExternalReloadPrompt = false;
        _isLoading = true;
        try
        {
            var html = await _vm.LoadPageContentAsync(_vm.CurrentPagePath);
            core.NavigateToString(html);
            _vm.StatusText = "Reloaded from disk";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void OnExternalKeepRequested(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _suppressExternalReloadPrompt = true;
            if (_vm != null)
                _vm.StatusText = "Keeping your edits; disk changes ignored until you save or navigate away.";
        }));
    }

    private void OnRequestNewFolder()
    {
        if (_vm == null) return;
        var parentPath = _vm.RootPath;
        if (NotesTreeView.SelectedItem is TreeViewItem tvi && tvi.Tag is TreeItemViewModel vm)
            parentPath = vm.Item.FullPath;
        if (string.IsNullOrEmpty(parentPath) || !Directory.Exists(parentPath)) return;
        if (!InputDialog.TryShow("Enter page name:", "New Page", out var name) || string.IsNullOrWhiteSpace(name)) return;
        EnsureAncestorsExpanded(parentPath);
        _rememberedExpandedPaths.Add(Path.Combine(parentPath, name.Trim()));
        ApplyRememberedExpansionToLiveTree();
        _vm.CreateFolder(parentPath, name.Trim());
        if (_vm.CurrentPagePath != null)
            _ = NavigateToAsync(_vm.CurrentPagePath, forceReload: true);
    }

    private void OnNotesTreeContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var treeViewItem = FindTreeViewItemUnderMouse(NotesTreeView);
        if (treeViewItem?.Tag is TreeItemViewModel vm)
        {
            treeViewItem.IsSelected = true;
            NotesTreeContextMenu.Tag = vm;
            var path = vm.Item.FullPath;
            NotesTreeOpenInExplorerMenuItem.IsEnabled =
                !string.IsNullOrEmpty(path) && Directory.Exists(path);
            var hasCloudId = _vm?.TryResolveRemoteId(path) is not null;
            var uiLang = _vm?.ShareSheet?.Lang;
            var notInCloudTip = ShareSheetStrings.NotInCloudYet(uiLang);
            NotesTreeShareMenuItem.Header = ShareSheetStrings.ShareMenu(uiLang);
            NotesTreeCopyLinkMenuItem.IsEnabled = hasCloudId;
            NotesTreeCopyLinkMenuItem.ToolTip = hasCloudId ? "Copy canonical Cloud link" : notInCloudTip;
            NotesTreeShareMenuItem.IsEnabled = hasCloudId && _vm?.ShareSheet != null;
            NotesTreeShareMenuItem.ToolTip = hasCloudId ? NotesTreeShareMenuItem.Header : notInCloudTip;
            NotesTreeOpenInBrowserMenuItem.IsEnabled = hasCloudId;
            NotesTreeOpenInBrowserMenuItem.ToolTip = hasCloudId ? "Open note in browser" : notInCloudTip;
            NotesTreeCopyIdMenuItem.IsEnabled = hasCloudId;
            NotesTreeCopyIdMenuItem.ToolTip = hasCloudId ? "Copy Cloud RemoteId" : notInCloudTip;

            if (hasCloudId && _vm != null)
                _ = _vm.PrefetchShareAffordanceAsync(path);
        }
        else
        {
            e.Handled = true;
        }
    }

    private static TreeViewItem? FindTreeViewItemUnderMouse(DependencyObject treeView)
    {
        var pos = Mouse.GetPosition((IInputElement)treeView);
        var element = VisualTreeHelper.HitTest(treeView as Visual ?? throw new InvalidOperationException(), pos)?.VisualHit;
        while (element != null)
        {
            if (element is TreeViewItem tvi)
                return tvi;
            element = VisualTreeHelper.GetParent(element) as DependencyObject;
        }
        return null;
    }

    private void OnNewPageClick(object sender, RoutedEventArgs e)
    {
        if (_vm == null || NotesTreeContextMenu.Tag is not TreeItemViewModel vm) return;
        var parentPath = vm.Item.FullPath;
        if (string.IsNullOrEmpty(parentPath)) return;
        if (!InputDialog.TryShow("Enter page name:", "New Page", out var name) || string.IsNullOrWhiteSpace(name)) return;
        EnsureAncestorsExpanded(parentPath);
        _rememberedExpandedPaths.Add(Path.Combine(parentPath, name.Trim()));
        ApplyRememberedExpansionToLiveTree();
        _vm.CreateFolder(parentPath, name.Trim());
        if (_vm.CurrentPagePath != null)
            _ = NavigateToAsync(_vm.CurrentPagePath, forceReload: true);
        NotesTreeContextMenu.Tag = null;
    }

    private void OnOpenInExplorerClick(object sender, RoutedEventArgs e)
    {
        if (_vm == null || NotesTreeContextMenu.Tag is not TreeItemViewModel vm) return;
        var folderPath = vm.Item.FullPath;
        NotesTreeContextMenu.Tag = null;
        if (!WindowsExplorer.TryOpenFolderSelected(folderPath))
            _vm.StatusText = "Folder not found";
    }

    private void OnCopyLinkClick(object sender, RoutedEventArgs e)
    {
        if (_vm == null || NotesTreeContextMenu.Tag is not TreeItemViewModel vm) return;
        var folderPath = vm.Item.FullPath;
        NotesTreeContextMenu.Tag = null;
        _vm.CopyLinkForPath(folderPath);
    }

    private void OnShareClick(object sender, RoutedEventArgs e)
    {
        if (_vm == null || NotesTreeContextMenu.Tag is not TreeItemViewModel vm) return;
        var folderPath = vm.Item.FullPath;
        NotesTreeContextMenu.Tag = null;
        _ = _vm.OpenShareForPathAsync(folderPath);
    }

    private void OnOpenInBrowserClick(object sender, RoutedEventArgs e)
    {
        if (_vm == null || NotesTreeContextMenu.Tag is not TreeItemViewModel vm) return;
        var folderPath = vm.Item.FullPath;
        NotesTreeContextMenu.Tag = null;
        _vm.OpenInBrowserForPath(folderPath);
    }

    private void OnCopyIdClick(object sender, RoutedEventArgs e)
    {
        if (_vm == null || NotesTreeContextMenu.Tag is not TreeItemViewModel vm) return;
        var folderPath = vm.Item.FullPath;
        NotesTreeContextMenu.Tag = null;

        var id = _vm.TryResolveRemoteId(folderPath);
        if (id is not { } remoteId)
        {
            _vm.StatusText = "Not in Cloud yet";
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(remoteId.ToString("D"));
            _vm.StatusText = "Id copied";
        }
        catch (Exception ex)
        {
            _vm.StatusText = "Could not copy id: " + ex.Message;
        }
    }

    private void OnRenameFolderClick(object sender, RoutedEventArgs e)
    {
        if (_vm == null || NotesTreeContextMenu.Tag is not TreeItemViewModel vm) return;
        var folderPath = vm.Item.FullPath;
        var currentName = vm.Name;
        if (string.IsNullOrEmpty(folderPath)) return;

        var rootPath = _vm.RootPath;
        if (!string.IsNullOrEmpty(rootPath) &&
            string.Equals(Path.GetFullPath(folderPath), Path.GetFullPath(rootPath), StringComparison.OrdinalIgnoreCase))
        {
            _vm.StatusText = "Cannot rename the notes root folder.";
            NotesTreeContextMenu.Tag = null;
            return;
        }

        if (!InputDialog.TryShow("Enter new page name:", "Rename Page", currentName, out var newName) ||
            string.IsNullOrWhiteSpace(newName))
        {
            NotesTreeContextMenu.Tag = null;
            return;
        }

        newName = newName.Trim();
        if (string.Equals(newName, currentName, StringComparison.OrdinalIgnoreCase))
        {
            NotesTreeContextMenu.Tag = null;
            return;
        }

        var wasCurrent = string.Equals(_vm.CurrentPagePath, folderPath, StringComparison.OrdinalIgnoreCase);
        if (_vm.RenameFolder(folderPath, newName))
        {
            if (wasCurrent && NotesWebView.CoreWebView2 != null && _vm.CurrentPagePath != null)
                _ = NavigateToAsync(_vm.CurrentPagePath);
        }

        NotesTreeContextMenu.Tag = null;
    }

    private void OnDeleteFolderClick(object sender, RoutedEventArgs e)
    {
        if (_vm == null || NotesTreeContextMenu.Tag is not TreeItemViewModel vm) return;

        var folderPath = vm.Item.FullPath;
        var name = vm.Name;
        if (string.IsNullOrEmpty(folderPath)) return;

        var rootPath = _vm.RootPath;
        if (!string.IsNullOrEmpty(rootPath) &&
            string.Equals(Path.GetFullPath(folderPath), Path.GetFullPath(rootPath), StringComparison.OrdinalIgnoreCase))
        {
            _vm.StatusText = "Cannot delete the notes root folder.";
            return;
        }

        var result = System.Windows.MessageBox.Show(
            $"Are you sure you want to delete page '{name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var wasCurrent = string.Equals(_vm.CurrentPagePath, folderPath, StringComparison.OrdinalIgnoreCase);
        if (_vm.DeleteFolder(folderPath))
        {
            if (wasCurrent && NotesWebView.CoreWebView2 != null)
                NotesWebView.CoreWebView2.NavigateToString("about:blank");
        }

        NotesTreeContextMenu.Tag = null;
    }

    private async void OnFormatBold(object sender, RoutedEventArgs e) => await RunFormatAsync(c => NotesWebFormatting.ApplyCommandAsync(c, "bold"));

    private async void OnFormatItalic(object sender, RoutedEventArgs e) => await RunFormatAsync(c => NotesWebFormatting.ApplyCommandAsync(c, "italic"));

    private async void OnFormatUnderline(object sender, RoutedEventArgs e) => await RunFormatAsync(c => NotesWebFormatting.ApplyCommandAsync(c, "underline"));

    private async void OnFormatStrikethrough(object sender, RoutedEventArgs e) => await RunFormatAsync(c => NotesWebFormatting.ApplyCommandAsync(c, "strikeThrough"));

    private async void OnFormatBulletList(object sender, RoutedEventArgs e) =>
        await RunFormatAsync(c => NotesWebFormatting.ApplyCommandAsync(c, "insertUnorderedList"));

    private async void OnFormatNumberedList(object sender, RoutedEventArgs e) =>
        await RunFormatAsync(c => NotesWebFormatting.ApplyCommandAsync(c, "insertOrderedList"));

    private async void OnFormatCheckboxList(object sender, RoutedEventArgs e) => await RunFormatAsync(NotesWebFormatting.ApplyCheckboxListAsync);

    private async void OnFormatInsertLink(object sender, RoutedEventArgs e)
    {
        if (NotesWebView.CoreWebView2 == null) return;
        if (!InputDialog.TryShow("Enter URL:", "Insert Link", "https://", out var url) || string.IsNullOrWhiteSpace(url)) return;
        try
        {
            await NotesWebFormatting.InsertLinkAsync(NotesWebView.CoreWebView2, url.Trim());
        }
        catch (Exception ex)
        {
            ErrorDetailDialog.Show("Insert link", ex);
        }
        finally
        {
            FocusEditor();
        }
    }

    private async void OnFormatCode(object sender, RoutedEventArgs e) => await RunFormatAsync(NotesWebFormatting.ApplyCodeBlockAsync);

    private async void OnFormatTable(object sender, RoutedEventArgs e) => await RunFormatAsync(NotesWebFormatting.InsertTableAsync);

    private async void OnHeadingComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_notesWebViewReady) return;
        if (HeadingCombo.SelectedItem is not ComboBoxItem item) return;
        var tag = item.Tag?.ToString() ?? "";
        var blockTag = string.IsNullOrEmpty(tag) ? "p" : tag;
        await RunFormatAsync(c => NotesWebFormatting.ApplyFormatBlockAsync(c, blockTag));
    }

    private async Task RunFormatAsync(Func<CoreWebView2, Task> action)
    {
        var core = NotesWebView.CoreWebView2;
        if (core == null) return;
        try
        {
            await action(core);
        }
        catch (Exception ex)
        {
            ErrorDetailDialog.Show("Formatting", ex);
        }
        finally
        {
            FocusEditor();
        }
    }

    /// <summary>
    /// Returns Windows keyboard focus to the WebView. The scripts call <c>editor.focus()</c>, but that only sets DOM
    /// focus — while a WPF control holds keyboard focus, no key reaches Chromium and typing and Ctrl+Z silently do
    /// nothing.
    /// </summary>
    private void FocusEditor() =>
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => NotesWebView.Focus()));
}
