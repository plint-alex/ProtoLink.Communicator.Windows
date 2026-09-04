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
using ProtoLink.Communicator.Windows.Services.Notes;
using ProtoLink.Communicator.Windows.Utilities;
using ProtoLink.Communicator.Windows.ViewModels;

namespace ProtoLink.Communicator.Windows.Views;

public partial class NotesView : System.Windows.Controls.UserControl
{
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

    public NotesView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnNotesVisibilityChanged;
        NotesWebView.SizeChanged += OnNotesWebViewSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm = DataContext as NotesViewModel;
        if (_vm != null)
        {
            _vm.TreeRefreshed += PopulateTree;
            _vm.RequestNewFolder += OnRequestNewFolder;
            _vm.InternalNoteWriteStarting += OnInternalNoteWriteStarting;
            _vm.InternalNoteWriteCompleted += OnInternalNoteWriteCompleted;
            _vm.NotesPagePathChanged += OnNotesPagePathChanged;
            _vm.NotesRootPathChanged += OnNotesRootPathChanged;
            _vm.ContentSaved += OnContentSaved;
            PopulateTree();
        }
        StartRootWatcher();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopIndexWatcher();
        StopRootWatcher();
        if (_vm != null)
        {
            _vm.TreeRefreshed -= PopulateTree;
            _vm.RequestNewFolder -= OnRequestNewFolder;
            _vm.InternalNoteWriteStarting -= OnInternalNoteWriteStarting;
            _vm.InternalNoteWriteCompleted -= OnInternalNoteWriteCompleted;
            _vm.NotesPagePathChanged -= OnNotesPagePathChanged;
            _vm.NotesRootPathChanged -= OnNotesRootPathChanged;
            _vm.ContentSaved -= OnContentSaved;
        }
    }

    private void OnNotesVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        TryStartWebViewInit();

    private void OnNotesWebViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
            TryStartWebViewInit();
    }

    /// <summary>Call when the Notes tab becomes selected (WebView2 needs a visible HWND).</summary>
    public void ActivateNotesTab() => TryStartWebViewInit();

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
        }));
    }

    private void OnNotesPagePathChanged(string newFolderPath)
    {
        if (Dispatcher.CheckAccess())
            AttachIndexWatcher(newFolderPath);
        else
            Dispatcher.BeginInvoke(new Action(() => AttachIndexWatcher(newFolderPath)));
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
        var expanded = CollectExpandedPaths();
        NotesTreeView.Items.Clear();
        if (_vm?.TreeRoot == null) return;
        var item = CreateTreeItem(_vm.TreeRoot, expanded);
        NotesTreeView.Items.Add(item);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(TryReselectCurrentPage));
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
            set.Add(vm.Item.FullPath);
        foreach (TreeViewItem child in node.Items.OfType<TreeViewItem>())
            CollectExpandedRecursive(child, set);
    }

    private static TreeViewItem CreateTreeItem(TreeItemViewModel vm, IReadOnlySet<string>? expandedPaths = null)
    {
        var item = new TreeViewItem { Header = vm.Name, Tag = vm };
        if (expandedPaths != null && expandedPaths.Contains(vm.Item.FullPath))
            item.IsExpanded = true;
        foreach (var child in vm.Children)
            item.Items.Add(CreateTreeItem(child, expandedPaths));
        return item;
    }

    private void TryReselectCurrentPage()
    {
        var path = _vm?.CurrentPagePath;
        if (string.IsNullOrEmpty(path)) return;
        foreach (TreeViewItem tvi in NotesTreeView.Items.OfType<TreeViewItem>())
        {
            if (TrySelectUnder(tvi, path))
                return;
        }
    }

    private static bool PathIsUnderOrEqual(string nodePath, string targetPath)
    {
        var n = Path.GetFullPath(nodePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var t = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(n, t, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = n + Path.DirectorySeparatorChar;
        return t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrySelectUnder(TreeViewItem node, string path)
    {
        if (node.Tag is not TreeItemViewModel vm) return false;
        var nodePath = vm.Item.FullPath;
        if (string.Equals(nodePath, path, StringComparison.OrdinalIgnoreCase))
        {
            node.IsSelected = true;
            node.Focus();
            return true;
        }
        if (!PathIsUnderOrEqual(nodePath, path)) return false;
        node.IsExpanded = true;
        foreach (TreeViewItem child in node.Items.OfType<TreeViewItem>())
        {
            if (TrySelectUnder(child, path))
                return true;
        }
        return false;
    }

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem tvi && tvi.Tag is TreeItemViewModel vm)
            _ = NavigateToAsync(vm.Item.FullPath);
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

    private async Task NavigateToAsync(string folderPath)
    {
        if (_vm == null || !Directory.Exists(folderPath)) return;

        var samePage = string.Equals(_vm.CurrentPagePath, folderPath, StringComparison.OrdinalIgnoreCase);
        if (samePage && NotesWebView.CoreWebView2 != null)
        {
            AttachIndexWatcher(folderPath);
            return;
        }

        StopIndexWatcher();
        _vm.CancelPendingSave();
        _suppressExternalReloadPrompt = false;
        _isLoading = true;
        try
        {
            _vm.CurrentPagePath = folderPath;
            var html = await _vm.LoadPageContentAsync(folderPath);
            if (NotesWebView.CoreWebView2 != null)
                NotesWebView.CoreWebView2.NavigateToString(html);

            AttachIndexWatcher(folderPath);
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
            _isLoading = true;
            try
            {
                var html = await _vm.LoadPageContentAsync(_vm.CurrentPagePath);
                core.NavigateToString(html);
                _vm.StatusText = "Reloaded from external change";
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
                // ignore
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
        if (string.IsNullOrEmpty(parentPath) || !Directory.Exists(parentPath))
        {
            if (NotesTreeView.SelectedItem is TreeViewItem tvi && tvi.Tag is TreeItemViewModel vm)
                parentPath = vm.Item.FullPath;
        }
        if (string.IsNullOrEmpty(parentPath)) return;
        if (!InputDialog.TryShow("Enter page name:", "New Page", out var name) || string.IsNullOrWhiteSpace(name)) return;
        _vm.CreateFolder(parentPath, name.Trim());
    }

    private void OnNotesTreeContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var treeViewItem = FindTreeViewItemUnderMouse(NotesTreeView);
        if (treeViewItem?.Tag is TreeItemViewModel vm)
        {
            treeViewItem.IsSelected = true;
            NotesTreeContextMenu.Tag = vm;
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
        _vm.CreateFolder(parentPath, name.Trim());
        NotesTreeContextMenu.Tag = null;
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
    }
}
