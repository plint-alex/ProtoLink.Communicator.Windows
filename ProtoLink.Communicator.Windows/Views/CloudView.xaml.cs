using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.ViewModels;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace ProtoLink.Communicator.Windows.Views;

public partial class CloudView : System.Windows.Controls.UserControl
{
    private CloudViewModel? _vm;

    public CloudView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm = DataContext as CloudViewModel;
        if (_vm != null)
        {
            _vm.ListScrollRestoreRequested += OnListScrollRestoreRequested;
            _vm.DownloadFile += OnDownloadFile;
            await _vm.EnsureRootAsync();
            // Defer sync so messenger contact GetEntities is not starved at startup.
            _ = StartDeferredSyncAsync();
        }
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
    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        var item = (sender as ListBoxItem)?.DataContext as CloudItemViewModel;
        _pressedItem = e.ClickCount == 1 ? item : null;
        if (e.ClickCount == 2 && item is { IsFolder: false })
            _ = _vm?.DownloadAsync(item);
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
        else if (key == Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ExecuteIfCan(_vm.CopyLinkCommand);
            e.Handled = true;
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
                var mapItem = new MenuItem { Header = "Map to local folder…" };
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
        menu.Items.Insert(2, new MenuItem { Header = "Open in browser", Command = _vm.OpenInBrowserCommand, CommandParameter = item });
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

    private void OnMapToLocalFolder(CloudItemViewModel folder)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select local folder to sync with",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        _vm!.AddMapping(folder.Id, dlg.SelectedPath, folder.Name);
    }

    private ContextMenu CreateBackgroundContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "New folder", Command = _vm!.NewFolderCommand });
        menu.Items.Add(new MenuItem { Header = "Upload", Command = _vm.UploadCommand });
        menu.Items.Add(new MenuItem { Header = "Refresh", Command = _vm.RefreshCommand });
        return menu;
    }

    private void OnUnmapSyncClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not CloudSyncMapping mapping) return;
        _vm?.RemoveMapping(mapping.CloudFolderId);
    }

    private void OnSyncNowClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not CloudSyncMapping mapping) return;
        _ = _vm?.SyncMappingAsync(mapping);
    }

    private void OnForceUploadClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not CloudSyncMapping mapping) return;
        _ = _vm?.ForceUploadMappingAsync(mapping);
    }

    private void OnForceDownloadClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not CloudSyncMapping mapping) return;
        _ = _vm?.ForceDownloadMappingAsync(mapping);
    }
}
