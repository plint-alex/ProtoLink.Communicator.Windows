using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Utilities;
using ProtoLink.Communicator.Windows.ViewModels;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace ProtoLink.Communicator.Windows.Views;

public partial class CloudView : System.Windows.Controls.UserControl
{
    private const double CompactBreakpoint = 700;
    private CloudViewModel? _vm;
    private GridLength _listColumnWidth = new(360);
    private bool _pdfWebViewReady;
    private string? _pdfNavigatedPath;

    public CloudView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        HookViewModel(DataContext as CloudViewModel);
        if (_vm != null)
        {
            await _vm.InitializeNavigationAsync();
            // Defer sync so messenger contact GetEntities is not starved at startup.
            _ = StartDeferredSyncAsync();
        }
        ApplyResponsiveLayout();
        RebuildEntityMoreMenu();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => UnhookViewModel();

    /// <summary>Release PDF WebView2 before process exit so native unload is orderly.</summary>
    public void ShutdownForAppExit()
    {
        _pdfWebViewReady = false;
        _pdfNavigatedPath = null;
        try
        {
            PdfPreviewWebView.Dispose();
        }
        catch
        {
            // Best-effort: browser process may already be gone during shutdown.
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UnhookViewModel();
        HookViewModel(e.NewValue as CloudViewModel);
        RebuildEntityMoreMenu();
    }

    private void HookViewModel(CloudViewModel? vm)
    {
        _vm = vm;
        if (_vm == null) return;
        _vm.ListScrollRestoreRequested += OnListScrollRestoreRequested;
        _vm.DownloadFile += OnDownloadFile;
        _vm.EntityOpened += OnEntityOpened;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void UnhookViewModel()
    {
        if (_vm == null) return;
        _vm.ListScrollRestoreRequested -= OnListScrollRestoreRequested;
        _vm.DownloadFile -= OnDownloadFile;
        _vm.EntityOpened -= OnEntityOpened;
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = null;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CloudViewModel.CurrentEntity)
            or nameof(CloudViewModel.HasCurrentFolderMapping)
            or nameof(CloudViewModel.CanMapCurrentFolder)
            or nameof(CloudViewModel.IsInSharedWithMeScope)
            or nameof(CloudViewModel.ShowSharedBrowseOnlyHint)
            or nameof(CloudViewModel.CanLeaveCurrentSharedRoot)
            or nameof(CloudViewModel.IsFolderOpen)
            or nameof(CloudViewModel.IsFileOpen))
            RebuildEntityMoreMenu();

        if (e.PropertyName is nameof(CloudViewModel.ShowListPane) or nameof(CloudViewModel.ShowEntityPane)
            or nameof(CloudViewModel.IsCompactLayout))
            ApplyColumnLayout();

        if (e.PropertyName is nameof(CloudViewModel.ShowPreviewPdf)
            or nameof(CloudViewModel.PreviewOpenPath))
            _ = UpdatePdfPreviewAsync();
    }

    private void OnEntityOpened()
    {
        if (_vm?.IsCompactLayout == true)
            _vm.ShowEntityInCompact = true;
    }

    private async Task StartDeferredSyncAsync()
    {
        try
        {
            await Task.Delay(1500);
            if (_vm != null)
                await _vm.SyncAllMappingsOnStartupAsync();
        }
        catch
        {
            // Sync errors are reported via CloudViewModel callback.
        }
    }

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
            if (ListColumn.Width.Value > 0)
                _listColumnWidth = ListColumn.Width;

            PaneSplitter.Visibility = Visibility.Collapsed;
            SplitterColumn.Width = new GridLength(0);

            if (_vm.ShowEntityInCompact)
            {
                ListColumn.Width = new GridLength(0);
                ListColumn.MinWidth = 0;
                EntityColumn.Width = new GridLength(1, GridUnitType.Star);
            }
            else
            {
                ListColumn.MinWidth = 0;
                ListColumn.MaxWidth = double.PositiveInfinity;
                ListColumn.Width = new GridLength(1, GridUnitType.Star);
                EntityColumn.Width = new GridLength(0);
            }
        }
        else
        {
            ListColumn.MinWidth = 320;
            ListColumn.MaxWidth = 420;
            ListColumn.Width = _listColumnWidth.Value > 0 ? _listColumnWidth : new GridLength(360);
            SplitterColumn.Width = new GridLength(4);
            EntityColumn.Width = new GridLength(1, GridUnitType.Star);
            PaneSplitter.Visibility = Visibility.Visible;
        }
    }

    private async Task UpdatePdfPreviewAsync()
    {
        if (_vm?.ShowPreviewPdf != true || string.IsNullOrEmpty(_vm.PreviewOpenPath) || !File.Exists(_vm.PreviewOpenPath))
        {
            if (_pdfWebViewReady && PdfPreviewWebView.CoreWebView2 != null)
                PdfPreviewWebView.CoreWebView2.Navigate("about:blank");
            _pdfNavigatedPath = null;
            return;
        }

        var path = _vm.PreviewOpenPath;
        if (string.Equals(_pdfNavigatedPath, path, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            await EnsurePdfWebViewAsync();
            if (PdfPreviewWebView.CoreWebView2 == null) return;
            var uri = new Uri(path).AbsoluteUri;
            PdfPreviewWebView.CoreWebView2.Navigate(uri);
            _pdfNavigatedPath = path;
        }
        catch
        {
            // Preview errors surface via VM StatusText / PreviewError when load fails upstream.
        }
    }

    private async Task EnsurePdfWebViewAsync()
    {
        if (_pdfWebViewReady && PdfPreviewWebView.CoreWebView2 != null) return;
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProtoLinkCommunicator",
            "WebView2-CloudPreview");
        Directory.CreateDirectory(userData);
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
        await PdfPreviewWebView.EnsureCoreWebView2Async(env);
        _pdfWebViewReady = PdfPreviewWebView.CoreWebView2 != null;
    }

    private void RebuildEntityMoreMenu()
    {
        if (_vm == null)
        {
            EntityHeaderControl.MoreMenu = null;
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Share…", Command = _vm.OpenShareCommand });
        menu.Items.Add(new MenuItem { Header = "Open in browser", Command = _vm.OpenInBrowserCommand });
        if (_vm.IsFileOpen)
        {
            menu.Items.Add(new MenuItem { Header = "Open", Command = _vm.OpenExternallyCommand });
            menu.Items.Add(new MenuItem { Header = "Download", Command = _vm.DownloadCurrentCommand });
        }
        menu.Items.Add(new Separator());

        if (_vm.IsFolderOpen)
        {
            if (_vm.CanMapCurrentFolder)
            {
                var mapItem = new MenuItem { Header = _vm.MapThisFolderLabel };
                mapItem.Click += (_, _) => OnMapCurrentFolderClick(this, new RoutedEventArgs());
                menu.Items.Add(mapItem);
            }

            if (_vm.ShowAddLocalFolderCta)
            {
                menu.Items.Add(new MenuItem
                {
                    Header = _vm.AddLocalFolderCtaLabel,
                    Command = _vm.AddLocalFolderCommand
                });
            }

            if (_vm.HasCurrentFolderMapping)
            {
                menu.Items.Add(new MenuItem { Header = "Sync", Command = _vm.SyncCurrentMappingCommand });
                menu.Items.Add(new MenuItem { Header = "Upload all", Command = _vm.ForceUploadCurrentMappingCommand });
                menu.Items.Add(new MenuItem { Header = "Download all", Command = _vm.ForceDownloadCurrentMappingCommand });
                menu.Items.Add(new MenuItem { Header = "Unmap", Command = _vm.UnmapCurrentFolderCommand });
                menu.Items.Add(new Separator());
            }
        }

        if (_vm.CanLeaveCurrentSharedRoot)
        {
            var lang = _vm.ShareSheet.Lang;
            var leaveItem = new MenuItem { Header = ShareSheetStrings.LeaveMenu(lang) };
            System.Windows.Automation.AutomationProperties.SetName(leaveItem, ShareSheetStrings.Leave(lang));
            leaveItem.Click += (_, _) => OnLeaveSharedCurrent();
            menu.Items.Add(leaveItem);
        }

        if (_vm.CurrentEntity?.ParentId != null)
        {
            var deleteItem = new MenuItem { Header = "Delete" };
            deleteItem.Click += (_, _) => OnDeleteCurrentEntity();
            menu.Items.Add(deleteItem);
        }

        EntityHeaderControl.MoreMenu = menu;
        EntityHeaderControl.ShowMoreMenu = menu.Items.Count > 0;
    }

    private void OnMapCurrentFolderClick(object sender, RoutedEventArgs e)
    {
        if (_vm?.CurrentEntity is not { IsFolder: true } folder) return;
        var path = FolderPicker.PickFolder("Select local folder to sync with");
        if (path is null) return;
        _vm.AddMapping(folder.Id, path, folder.Name);
    }

    private void OnDeleteCurrentEntity()
    {
        if (_vm?.CurrentEntity is null) return;
        var entity = _vm.CurrentEntity;
        var message = entity.IsFolder
            ? $"Delete folder \"{entity.Name}\" and all its contents?"
            : $"Delete file \"{entity.Name}\"?";
        if (System.Windows.MessageBox.Show(message, "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _vm.DeleteCurrentCommand.Execute(null);
    }

    private void OnListScrollRestoreRequested(double offset)
    {
        Dispatcher.BeginInvoke(
            () => FindScrollViewer(CloudListBox)?.ScrollToVerticalOffset(offset),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } found)
                return found;
        }
        return null;
    }

    private void OnListScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_vm != null) _vm.ListScrollOffset = e.VerticalOffset;
    }

    private void OnDownloadFile(string fileName, Stream stream)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = fileName };
        if (dlg.ShowDialog() != true) return;
        using var file = File.Create(dlg.FileName);
        stream.CopyTo(file);
        _vm!.StatusText = "Downloaded";
    }

    private CloudItemViewModel? _pressedItem;

    /// <summary>Single click opens. The second press of a double-click must not open the row that replaced the clicked folder.</summary>
    private void OnFolderTreeDoubleClick(object sender, MouseButtonEventArgs e) =>
        OpenFolderTreeFromSender(e.OriginalSource as DependencyObject);

    private void OnFolderTreeItemClick(object sender, MouseButtonEventArgs e) =>
        OpenFolderTreeFromSender(e.OriginalSource as DependencyObject);

    private void OpenFolderTreeFromSender(DependencyObject? source)
    {
        if (_vm == null || source == null) return;
        var row = FindFolderTreeRow(source);
        if (row == null) return;
        if (_vm.OpenFolderTreeCommand.CanExecute(row))
            _vm.OpenFolderTreeCommand.Execute(row);
    }

    private static CloudFolderTreeRowViewModel? FindFolderTreeRow(DependencyObject? start)
    {
        for (var d = start; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement { DataContext: CloudFolderTreeRowViewModel row })
                return row;
        }
        return null;
    }

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        var item = (sender as ListBoxItem)?.DataContext as CloudItemViewModel;
        _pressedItem = e.ClickCount == 1 ? item : null;
    }

    private void OnItemMouseUp(object sender, MouseButtonEventArgs e)
    {
        var item = (sender as ListBoxItem)?.DataContext as CloudItemViewModel;
        if (item != null && ReferenceEquals(item, _pressedItem))
            _vm?.OpenCommand.Execute(item);
        _pressedItem = null;
    }

    private void OnListKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || CloudListBox.SelectedItem is not CloudItemViewModel item) return;
        _vm?.OpenCommand.Execute(item);
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_vm == null) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            ExecuteIfCan(_vm.BackCommand);
            e.Handled = true;
        }
        else if (key == Key.Escape)
        {
            _vm.HandleEscape();
            FocusList();
            e.Handled = true;
        }
        else if (key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ListSearchBox.Focus();
            ListSearchBox.SelectAll();
            e.Handled = true;
        }
        else if (key == Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ExecuteIfCan(_vm.CopyLinkCommand);
            e.Handled = true;
        }
    }

    private void FocusList()
    {
        CloudListBox.Focus();
        if (CloudListBox.SelectedItem != null)
        {
            var container = CloudListBox.ItemContainerGenerator.ContainerFromItem(CloudListBox.SelectedItem) as ListBoxItem;
            container?.Focus();
        }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.XButton1 || _vm == null) return;
        ExecuteIfCan(_vm.BackCommand);
        e.Handled = true;
    }

    private static void ExecuteIfCan(ICommand command)
    {
        if (command.CanExecute(null)) command.Execute(null);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is CloudItemViewModel item)
            CloudListBox.ContextMenu = CreateItemContextMenu(item);
        else
            CloudListBox.ContextMenu = CreateBackgroundContextMenu();
    }

    private ContextMenu CreateItemContextMenu(CloudItemViewModel item)
    {
        var menu = new ContextMenu();
        if (item.IsFolder)
        {
            menu.Items.Add(new MenuItem { Header = "Open", Command = _vm!.OpenCommand, CommandParameter = item });
            menu.Items.Add(new MenuItem { Header = "New subfolder", Command = _vm.NewSubfolderCommand, CommandParameter = item });
            menu.Items.Add(new MenuItem { Header = "Upload here", Command = _vm.UploadHereCommand, CommandParameter = item });
            menu.Items.Add(new Separator());
            if (_vm.CanMapFolder(item))
            {
                var mapItem = new MenuItem { Header = _vm!.MapThisFolderLabel };
                mapItem.Click += (s, e) => OnMapToLocalFolder(item);
                menu.Items.Add(mapItem);
            }
            if (_vm.IsFolderSynced(item.Id))
            {
                var mapping = _vm.SyncMappings.FirstOrDefault(m => m.CloudFolderId == item.Id);
                if (mapping != null)
                {
                    var syncNowItem = new MenuItem { Header = "Sync now" };
                    syncNowItem.Click += (s, e) => _ = _vm.SyncMappingAsync(mapping);
                    menu.Items.Add(syncNowItem);
                    var uploadAll = new MenuItem { Header = "Upload all" };
                    uploadAll.Click += (s, e) => _ = _vm.ForceUploadMappingAsync(mapping);
                    menu.Items.Add(uploadAll);
                    var downloadAll = new MenuItem { Header = "Download all" };
                    downloadAll.Click += (s, e) => _ = _vm.ForceDownloadMappingAsync(mapping);
                    menu.Items.Add(downloadAll);
                }
                var unmapItem = new MenuItem { Header = "Unmap" };
                unmapItem.Click += (s, e) => _vm.RemoveMapping(item.Id);
                menu.Items.Add(unmapItem);
            }
            AddDeleteMenuItem(menu, item);
        }
        else
        {
            menu.Items.Add(new MenuItem { Header = "Open", Command = _vm!.OpenCommand, CommandParameter = item });
            var downloadItem = new MenuItem { Header = "Download" };
            downloadItem.Click += (s, e) => _ = _vm.DownloadAsync(item);
            menu.Items.Add(downloadItem);
            menu.Items.Add(new Separator());
            AddDeleteMenuItem(menu, item);
        }
        menu.Items.Insert(1, new MenuItem { Header = "Copy link", Command = _vm.CopyLinkCommand, CommandParameter = item });
        menu.Items.Insert(2, new MenuItem { Header = _vm.CopyIdLabel, Command = _vm.CopyIdCommand, CommandParameter = item });
        menu.Items.Insert(3, new MenuItem { Header = "Share…", Command = _vm.OpenShareCommand, CommandParameter = item });
        menu.Items.Insert(4, new MenuItem { Header = "Open in browser", Command = _vm.OpenInBrowserCommand, CommandParameter = item });
        if (_vm.CanLeaveSharedRow(item))
        {
            var lang = _vm.ShareSheet.Lang;
            var leaveItem = new MenuItem { Header = ShareSheetStrings.Leave(lang) };
            System.Windows.Automation.AutomationProperties.SetName(leaveItem, ShareSheetStrings.Leave(lang));
            leaveItem.Click += (_, _) => OnLeaveSharedItem(item);
            menu.Items.Add(leaveItem);
        }
        return menu;
    }

    private void AddDeleteMenuItem(ContextMenu menu, CloudItemViewModel item)
    {
        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += (s, e) => OnDeleteItem(item);
        menu.Items.Add(deleteItem);
    }

    private void OnDeleteItem(CloudItemViewModel item)
    {
        var message = item.IsFolder
            ? $"Delete folder \"{item.Name}\" and all its contents?"
            : $"Delete file \"{item.Name}\"?";
        if (System.Windows.MessageBox.Show(message, "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _vm!.DeleteCommand.Execute(item);
    }

    private void OnLeaveSharedItem(CloudItemViewModel item)
    {
        if (_vm == null || !ConfirmLeaveShared()) return;
        _ = _vm.LeaveSharedAsync(item.Id);
    }

    private void OnLeaveSharedCurrent()
    {
        if (_vm?.CurrentEntity is null || !ConfirmLeaveShared()) return;
        _ = _vm.LeaveSharedAsync(_vm.CurrentEntity.Id);
    }

    private bool ConfirmLeaveShared()
    {
        var lang = _vm!.ShareSheet.Lang;
        var result = System.Windows.MessageBox.Show(
            ShareSheetStrings.LeaveConfirm(lang),
            ShareSheetStrings.Leave(lang),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        // OK = Leave; Cancel keeps grant and row (UX-LS-2).
        return result == MessageBoxResult.OK;
    }

    private void OnMapToLocalFolder(CloudItemViewModel folder)
    {
        var path = FolderPicker.PickFolder("Select local folder to sync with");
        if (path is null) return;
        _vm!.AddMapping(folder.Id, path, folder.Name);
    }

    private ContextMenu CreateBackgroundContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "New folder", Command = _vm!.NewFolderCommand });
        menu.Items.Add(new MenuItem { Header = "Upload", Command = _vm.UploadCommand });
        menu.Items.Add(new MenuItem { Header = "Refresh", Command = _vm.RefreshCommand });
        return menu;
    }

    private void OnCreateFabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.ContextMenu is null) return;
        // ContextMenu is not in the visual tree — inherit VM for command/header bindings.
        btn.ContextMenu.DataContext = DataContext;
        btn.ContextMenu.PlacementTarget = btn;
        btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        btn.ContextMenu.IsOpen = true;
        e.Handled = true;
    }
}
