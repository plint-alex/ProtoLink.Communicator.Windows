using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using ProtoLink.Communicator.Windows.Dialogs;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Utilities;
using ProtoLink.Communicator.Windows.ViewModels;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace ProtoLink.Communicator.Windows.Views;

public partial class CloudView : System.Windows.Controls.UserControl
{
    private const double CompactBreakpoint = CloudExplorerColumnLayout.CompactBreakpoint;
    private CloudViewModel? _vm;
    private GridLength _treeColumnWidth = new(220);
    private GridLength _stackedListRowHeight = new(1, GridUnitType.Star);
    private GridLength _stackedEntityRowHeight = new(1, GridUnitType.Star);
    private bool _pdfWebViewReady;
    private string? _pdfNavigatedPath;
    /// <summary>TabControl remounts this view; skip full GetEntities cold-nav on every switch.</summary>
    private bool _initialNavigationDone;
    private bool _deferredSyncStarted;

    public CloudView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        HookViewModel(DataContext as CloudViewModel);
        // Layout only — InitializeNavigationAsync / sync must not block tab chrome paint.
        ApplyResponsiveLayout();
        RebuildEntityMoreMenu();
        if (_vm == null)
            return;

        if (!_initialNavigationDone && _vm.CurrentEntity == null)
            _vm.StatusText = "Loading...";

        ShellTabActivation.ScheduleAfterChromePaint(Dispatcher, ActivateCloudSurfaceAfterChromePaint);
    }

    private async Task ActivateCloudSurfaceAfterChromePaint()
    {
        if (!IsLoaded || _vm == null)
            return;

        if (!_initialNavigationDone)
        {
            try
            {
                await _vm.InitializeNavigationAsync();
            }
            finally
            {
                _initialNavigationDone = true;
            }
        }

        // One deferred mapped-sync kickoff per view instance (014cd52 Sync lane; not on Interactive).
        if (!_deferredSyncStarted)
        {
            _deferredSyncStarted = true;
            _ = StartDeferredSyncAsync();
        }
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
        EntityHeaderControl.MoreMenuOpening += RebuildEntityMoreMenu;
    }

    private void UnhookViewModel()
    {
        EntityHeaderControl.MoreMenuOpening -= RebuildEntityMoreMenu;
        if (_vm == null) return;
        _vm.ListScrollRestoreRequested -= OnListScrollRestoreRequested;
        _vm.DownloadFile -= OnDownloadFile;
        _vm.EntityOpened -= OnEntityOpened;
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = null;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // CIP1: Locations ⋮ rebuilt on MoreMenuOpening (no action-menu PropertyChanged fan-out).

        if (e.PropertyName is nameof(CloudViewModel.ShowListPane) or nameof(CloudViewModel.ShowEntityPane)
            or nameof(CloudViewModel.IsCompactLayout)
            or nameof(CloudViewModel.ShowContentsInCompact)
            or nameof(CloudViewModel.ShowFolderTree)
            or nameof(CloudViewModel.CompactBrowsePanel))
            ApplyColumnLayout();

        if (e.PropertyName is nameof(CloudViewModel.ShowPreviewPdf)
            or nameof(CloudViewModel.PreviewOpenPath))
            _ = UpdatePdfPreviewAsync();
    }

    private void OnEntityOpened()
    {
        if (_vm?.IsCompactLayout != true) return;
        // E9h: any open (tree folder / list / Locations) → contents panel.
        _vm.EnterCompactContents();
        // Files (and entity-focused opens) show info/preview; folders stay on the listing.
        _vm.ShowEntityInCompact = _vm.IsFileOpen;
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
            if (TreeColumn.Width.Value > 0)
                _treeColumnWidth = TreeColumn.Width;

            var panel = _vm.CompactBrowsePanel;
            var showTree = CloudExplorerColumnLayout.ShouldShowTreePanel(
                true, panel, _vm.ShowFolderTree);
            var showContents = CloudExplorerColumnLayout.ShouldShowContentsPanel(
                true, panel, _vm.ShowFolderTree);

            if (showTree && !showContents)
            {
                // E9h AT-CE24/CE25: Folders tree full width; main collapsed.
                ExpandTreeColumnFullWidth();
                CollapseMainColumn();
                return;
            }

            // E9h contents (or empty-tree E9d): main full width; tree collapsed.
            CollapseTreeColumn();
            ExpandMainColumn();
            ApplySideBySideBrowsePanes(compactToggle: true);

            if (_vm.ShowEntityInCompact)
            {
                ListColumn.Width = new GridLength(0);
                ListColumn.MinWidth = 0;
                EntityColumn.MinWidth = 0;
                EntityColumn.Width = new GridLength(1, GridUnitType.Star);
            }
            else
            {
                ListColumn.MinWidth = 0;
                ListColumn.Width = new GridLength(1, GridUnitType.Star);
                EntityColumn.Width = new GridLength(0);
                EntityColumn.MinWidth = 0;
            }
            return;
        }

        ExpandMainColumn();

        // Wide: Folders column only when tree has rows (E9d / AT-CE15).
        // E9e / AT-CE16: with tree → Tree | stacked contents+info; without → contents | info.
        if (!CloudExplorerColumnLayout.ShouldReserveTreeColumn(_vm.IsCompactLayout, _vm.ShowFolderTree))
        {
            if (TreeColumn.Width.Value > 0)
                _treeColumnWidth = TreeColumn.Width;
            CollapseTreeColumn();
            ApplySideBySideBrowsePanes(compactToggle: false);
            return;
        }

        TreeColumn.MinWidth = 160;
        TreeColumn.MaxWidth = 360;
        TreeColumn.Width = _treeColumnWidth.Value > 0 ? _treeColumnWidth : new GridLength(220);
        TreeSplitterColumn.Width = new GridLength(4);
        TreePaneSplitter.Visibility = Visibility.Visible;
        ApplyStackedBrowsePanes();
    }

    private void CollapseTreeColumn()
    {
        TreeColumn.Width = new GridLength(0);
        TreeColumn.MinWidth = 0;
        TreeColumn.MaxWidth = 0;
        TreeSplitterColumn.Width = new GridLength(0);
        TreePaneSplitter.Visibility = Visibility.Collapsed;
    }

    /// <summary>E9h narrow tree panel: Folders column fills the shell.</summary>
    private void ExpandTreeColumnFullWidth()
    {
        TreeColumn.MinWidth = 0;
        TreeColumn.MaxWidth = double.PositiveInfinity;
        TreeColumn.Width = new GridLength(1, GridUnitType.Star);
        TreeSplitterColumn.Width = new GridLength(0);
        TreePaneSplitter.Visibility = Visibility.Collapsed;
    }

    private void CollapseMainColumn()
    {
        MainColumn.Width = new GridLength(0);
        MainColumn.MinWidth = 0;
    }

    private void ExpandMainColumn()
    {
        MainColumn.MinWidth = 280;
        MainColumn.Width = new GridLength(1, GridUnitType.Star);
    }

    /// <summary>Empty-tree / compact: contents | info as two columns inside MainPanesGrid.</summary>
    private void ApplySideBySideBrowsePanes(bool compactToggle)
    {
        RememberStackedRowHeightsIfActive();

        BrowseRowTop.Height = new GridLength(1, GridUnitType.Star);
        BrowseRowSplitter.Height = new GridLength(0);
        BrowseRowBottom.Height = new GridLength(0);

        Grid.SetRow(ListPane, 0);
        Grid.SetColumn(ListPane, 0);
        Grid.SetColumnSpan(ListPane, 1);
        Grid.SetRowSpan(ListPane, 1);

        Grid.SetRow(EntityPaneSplitter, 0);
        Grid.SetColumn(EntityPaneSplitter, 1);
        Grid.SetColumnSpan(EntityPaneSplitter, 1);
        Grid.SetRowSpan(EntityPaneSplitter, 1);
        EntityPaneSplitter.Width = 4;
        EntityPaneSplitter.Height = double.NaN;
        EntityPaneSplitter.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        EntityPaneSplitter.VerticalAlignment = VerticalAlignment.Stretch;
        EntityPaneSplitter.ResizeDirection = GridResizeDirection.Columns;
        EntityPaneSplitter.ResizeBehavior = GridResizeBehavior.PreviousAndNext;

        Grid.SetRow(EntityPane, 0);
        Grid.SetColumn(EntityPane, 2);
        Grid.SetColumnSpan(EntityPane, 1);
        Grid.SetRowSpan(EntityPane, 1);

        if (compactToggle)
        {
            EntitySplitterColumn.Width = new GridLength(0);
            EntityPaneSplitter.Visibility = Visibility.Collapsed;
            return;
        }

        ListColumn.MinWidth = 280;
        ListColumn.Width = new GridLength(1, GridUnitType.Star);
        EntitySplitterColumn.Width = new GridLength(4);
        EntityPaneSplitter.Visibility = Visibility.Visible;
        EntityColumn.MinWidth = 240;
        EntityColumn.Width = new GridLength(1, GridUnitType.Star);
    }

    /// <summary>Populated tree (E9e): stack contents above info inside one main pane.</summary>
    private void ApplyStackedBrowsePanes()
    {
        ListColumn.MinWidth = 0;
        ListColumn.Width = new GridLength(1, GridUnitType.Star);
        EntitySplitterColumn.Width = new GridLength(0);
        EntityColumn.MinWidth = 0;
        EntityColumn.Width = new GridLength(0);

        BrowseRowTop.Height = _stackedListRowHeight.Value > 0 || _stackedListRowHeight.IsStar
            ? _stackedListRowHeight
            : new GridLength(1, GridUnitType.Star);
        BrowseRowSplitter.Height = new GridLength(4);
        BrowseRowBottom.Height = _stackedEntityRowHeight.Value > 0 || _stackedEntityRowHeight.IsStar
            ? _stackedEntityRowHeight
            : new GridLength(1, GridUnitType.Star);

        Grid.SetRow(ListPane, 0);
        Grid.SetColumn(ListPane, 0);
        Grid.SetColumnSpan(ListPane, 3);
        Grid.SetRowSpan(ListPane, 1);

        Grid.SetRow(EntityPaneSplitter, 1);
        Grid.SetColumn(EntityPaneSplitter, 0);
        Grid.SetColumnSpan(EntityPaneSplitter, 3);
        Grid.SetRowSpan(EntityPaneSplitter, 1);
        EntityPaneSplitter.Height = 4;
        EntityPaneSplitter.Width = double.NaN;
        EntityPaneSplitter.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        EntityPaneSplitter.VerticalAlignment = VerticalAlignment.Stretch;
        EntityPaneSplitter.ResizeDirection = GridResizeDirection.Rows;
        EntityPaneSplitter.ResizeBehavior = GridResizeBehavior.PreviousAndNext;
        EntityPaneSplitter.Visibility = Visibility.Visible;

        Grid.SetRow(EntityPane, 2);
        Grid.SetColumn(EntityPane, 0);
        Grid.SetColumnSpan(EntityPane, 3);
        Grid.SetRowSpan(EntityPane, 1);
    }

    private void RememberStackedRowHeightsIfActive()
    {
        if (BrowseRowSplitter.Height.Value <= 0)
            return;
        if (BrowseRowTop.Height.Value > 0 || BrowseRowTop.Height.IsStar)
            _stackedListRowHeight = BrowseRowTop.Height;
        if (BrowseRowBottom.Height.Value > 0 || BrowseRowBottom.Height.IsStar)
            _stackedEntityRowHeight = BrowseRowBottom.Height;
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

    /// <summary>
    /// CIP1: EntityHeader ⋮ is Locations-only (E9f SWM / SBM / Recent).
    /// Folder/file actions live on list + Folders-tree context menus.
    /// </summary>
    private void RebuildEntityMoreMenu()
    {
        if (_vm == null)
        {
            EntityHeaderControl.MoreMenu = null;
            EntityHeaderControl.ShowMoreMenu = false;
            return;
        }

        var menu = new ContextMenu();
        var swmItem = new MenuItem
        {
            Header = _vm.OpenSharedWithMeLabel,
            Command = _vm.OpenSharedWithMeCommand
        };
        AutomationProperties.SetName(swmItem, "CloudLocationsSharedWithMe");
        menu.Items.Add(swmItem);
        var sbmItem = new MenuItem
        {
            Header = _vm.OpenSharedByMeLabel,
            Command = _vm.OpenSharedByMeCommand
        };
        AutomationProperties.SetName(sbmItem, "CloudLocationsSharedByMe");
        menu.Items.Add(sbmItem);
        var recentItem = new MenuItem
        {
            Header = _vm.OpenRecentLabel,
            Command = _vm.OpenRecentCommand
        };
        AutomationProperties.SetName(recentItem, "CloudLocationsRecent");
        menu.Items.Add(recentItem);

        EntityHeaderControl.MoreMenu = menu;
        EntityHeaderControl.ShowMoreMenu = true;
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
    private void OnFolderTreeExpandPreviewDown(object sender, MouseButtonEventArgs e)
    {
        // Expand/collapse only — do not open the folder (AT-CE10/CE11).
        e.Handled = true;
        if (sender is System.Windows.Controls.Button { Command: { } cmd, CommandParameter: { } param }
            && cmd.CanExecute(param))
            cmd.Execute(param);
    }

    private void OnFolderTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // PreviewMouse* tunnels ListBox→button; expand affordance must not open (1.0.102 wipe).
        if (IsFolderTreeExpandAffordance(e.OriginalSource as DependencyObject)) return;
        OpenFolderTreeFromSender(e.OriginalSource as DependencyObject);
    }

    private void OnFolderTreeItemClick(object sender, MouseButtonEventArgs e)
    {
        if (IsFolderTreeExpandAffordance(e.OriginalSource as DependencyObject)) return;
        OpenFolderTreeFromSender(e.OriginalSource as DependencyObject);
    }

    /// <summary>CIP1: select the row under the cursor so the tree ContextMenu targets that folder.</summary>
    private void OnFolderTreePreviewRightDown(object sender, MouseButtonEventArgs e)
    {
        if (IsFolderTreeExpandAffordance(e.OriginalSource as DependencyObject)) return;
        var row = FindFolderTreeRow(e.OriginalSource as DependencyObject);
        if (row == null || _vm == null) return;
        FolderTreeListBox.SelectedItem = row;
        foreach (var r in _vm.FolderTreeRows)
            r.IsSelected = r.Id == row.Id;
    }

    private void OnFolderTreeContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_vm == null)
        {
            e.Handled = true;
            return;
        }

        var row = FolderTreeListBox.SelectedItem as CloudFolderTreeRowViewModel
                  ?? FindFolderTreeRow(e.OriginalSource as DependencyObject);
        if (row == null)
        {
            e.Handled = true;
            return;
        }

        FolderTreeListBox.ContextMenu = CreateItemContextMenu(FolderTreeRowAsItem(row));
    }

    private CloudItemViewModel FolderTreeRowAsItem(CloudFolderTreeRowViewModel row)
    {
        var item = new CloudItemViewModel
        {
            Id = row.Id,
            Name = row.Name,
            IsFolder = true
        };
        if (_vm != null)
            item.IsShareRoot = _vm.IsSwmShareRootItem(item);
        return item;
    }

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

    /// <summary>
    /// ListBox uses PreviewMouseLeftButtonUp (tunnel). Expand Button PreviewDown Handled does not
    /// stop that tunnel — without this filter every expand also OpenFolderTree'd and listing refresh
    /// wiped children (tester 1.0.102 Session-1).
    /// </summary>
    private static bool IsFolderTreeExpandAffordance(DependencyObject? start)
    {
        for (var d = start; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is System.Windows.Controls.Button btn)
            {
                var name = AutomationProperties.GetName(btn);
                if (string.Equals(name, "Expand or collapse folder", StringComparison.Ordinal))
                    return true;
            }

            if (d is System.Windows.Controls.ListBoxItem)
                break;
        }

        return false;
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
        else if (key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            ExecuteIfCan(_vm.RefreshCommand);
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
        if (_vm == null || !_vm.CanDeleteItem(item)) return;
        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += (s, e) => OnDeleteItem(item);
        menu.Items.Add(deleteItem);
    }

    private void OnDeleteItem(CloudItemViewModel item) => _ = ConfirmAndDeleteAsync(item);

    private async Task ConfirmAndDeleteAsync(CloudItemViewModel item)
    {
        if (_vm == null) return;
        var lang = _vm.ShareSheet.Lang;
        var shared = await _vm.ResolveIsSharedDeleteTargetAsync(item);
        if (shared)
        {
            var choice = SharedCloudDeleteDialog.Show(
                lang,
                item.Name,
                item.IsFolder,
                canWriteForAll: _vm.CanDeleteItem(item));
            if (choice == SharedCloudDeleteChoice.ForMeOnly)
                await _vm.DeleteSharedForMeOnlyAsync(item);
            else if (choice == SharedCloudDeleteChoice.ForAll)
                await _vm.DeleteSharedForAllAsync(item);
            return;
        }

        var message = item.IsFolder
            ? $"Delete folder \"{item.Name}\" and all its contents?"
            : $"Delete file \"{item.Name}\"?";
        if (System.Windows.MessageBox.Show(
                message,
                "Confirm delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        if (_vm.DeleteCommand.CanExecute(item))
            _vm.DeleteCommand.Execute(item);
    }

    private void OnLeaveSharedItem(CloudItemViewModel item)
    {
        if (_vm == null || !ConfirmLeaveShared()) return;
        _ = _vm.LeaveSharedAsync(item.Id);
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
