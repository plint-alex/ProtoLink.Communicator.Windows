using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.ViewModels;

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
            _vm.NavigateToFolder += OnNavigateToFolder;
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

    private void OnNavigateToFolder(Guid folderId, string folderName)
    {
        _ = _vm?.NavigateToAsync(folderId, folderName);
    }

    private void OnDownloadFile(string fileName, Stream stream)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = fileName };
        if (dlg.ShowDialog() != true) return;
        using var file = File.Create(dlg.FileName);
        stream.CopyTo(file);
        _vm!.StatusText = "Downloaded";
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CloudListBox.SelectedItem is not CloudItemViewModel item) return;
        _ = _vm?.DownloadAsync(item);
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
            menu.Items.Add(new MenuItem { Header = "Download", Command = _vm!.OpenCommand, CommandParameter = item });
            menu.Items.Add(new Separator());
            AddDeleteMenuItem(menu, item);
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
}
