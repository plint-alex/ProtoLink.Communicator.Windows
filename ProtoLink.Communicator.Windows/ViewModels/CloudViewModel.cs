using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows.Input;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;

namespace ProtoLink.Communicator.Windows.ViewModels;

public class CloudViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly ISettingsService _settingsService;
    private readonly CloudApiService _api;
    private readonly CloudMappedSyncCoordinator _mappedSync;
    private readonly ISyncMappingStore _syncStore;
    private readonly Action<Exception>? _syncFullErrorReporter;
    private Guid? _currentFolderId;
    private CloudItemViewModel? _selectedItem;
    private string _statusText = "Ready";

    public CloudViewModel(IAuthService authService, HttpClient httpClient, ISyncMappingStore syncStore, ISettingsService settingsService, Action<Exception>? syncFullErrorReporter = null)
    {
        _authService = authService;
        _settingsService = settingsService;
        _api = new CloudApiService(httpClient);
        var folderSynchronizer = new CloudFolderSynchronizer(_api);
        _syncStore = syncStore;
        _syncFullErrorReporter = syncFullErrorReporter;
        Items = new ObservableCollection<CloudItemViewModel>();
        BreadcrumbPath = new ObservableCollection<CloudBreadcrumbEntry>();
        SyncMappings = new ObservableCollection<CloudSyncMapping>(_syncStore.Load());
        _mappedSync = new CloudMappedSyncCoordinator(
            folderSynchronizer,
            SyncMappings,
            LoadCurrentAsync,
            s => StatusText = s,
            _syncFullErrorReporter,
            System.Windows.Application.Current.Dispatcher);
        _mappedSync.RestartFileWatchers();
        RefreshCommand = new RelayCommand(async _ => await LoadCurrentAsync());
        NewFolderCommand = new RelayCommand(_ => NewFolder(), _ => _currentFolderId.HasValue);
        UploadCommand = new RelayCommand(_ => Upload(), _ => _currentFolderId.HasValue);
        DeleteCommand = new RelayCommand(async p => await DeleteAsync((CloudItemViewModel)p!), p => p is CloudItemViewModel);
        UpCommand = new RelayCommand(_ => GoUp(), _ => CanGoUp);
        NavigateToBreadcrumbCommand = new RelayCommand(p => NavigateToBreadcrumb((int)p!));
        OpenCommand = new RelayCommand(p => OpenItem((CloudItemViewModel)p!), p => p is CloudItemViewModel);
        NewSubfolderCommand = new RelayCommand(p => NewSubfolder((CloudItemViewModel)p!), p => p is CloudItemViewModel item && item.IsFolder);
        UploadHereCommand = new RelayCommand(p => UploadHere((CloudItemViewModel)p!), p => p is CloudItemViewModel item && item.IsFolder);
        DownloadCommand = new RelayCommand(
            _ => { if (SelectedItem != null && !SelectedItem.IsFolder) _ = DownloadAsync(SelectedItem); },
            _ => SelectedItem != null && !SelectedItem.IsFolder);
        OpenWebExplorerCommand = new RelayCommand(_ => OpenWebExplorerInBrowser(), _ => _currentFolderId.HasValue);
    }

    public ObservableCollection<CloudItemViewModel> Items { get; }
    public ObservableCollection<CloudBreadcrumbEntry> BreadcrumbPath { get; }
    public ObservableCollection<CloudSyncMapping> SyncMappings { get; }
    public bool CanGoUp => BreadcrumbPath.Count > 1;
    public bool IsEmpty => Items.Count == 0;
    public Guid? CurrentFolderId
    {
        get => _currentFolderId;
        private set
        {
            _currentFolderId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanGoUp));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public ICommand RefreshCommand { get; }
    public ICommand NewFolderCommand { get; }
    public ICommand UploadCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand NavigateToBreadcrumbCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand NewSubfolderCommand { get; }
    public ICommand UploadHereCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand OpenWebExplorerCommand { get; }
    public event Action<Guid, string>? NavigateToFolder;

    public CloudItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (ReferenceEquals(_selectedItem, value)) return;
            _selectedItem = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public event Action<string, Stream>? DownloadFile;

    public bool IsFolderSynced(Guid folderId) => SyncMappings.Any(m => m.CloudFolderId == folderId);

    public bool CanMapFolder(CloudItemViewModel item)
    {
        if (!item.IsFolder) return false;
        if (IsFolderSynced(item.Id)) return false;
        return BreadcrumbPath.All(b => !IsFolderSynced(b.Id));
    }

    public void AddMapping(Guid cloudFolderId, string localPath, string? cloudFolderName = null)
    {
        if (string.IsNullOrWhiteSpace(localPath)) return;
        if (SyncMappings.Any(m => m.CloudFolderId == cloudFolderId)) return;
        var ancestorSynced = BreadcrumbPath.Any(b => IsFolderSynced(b.Id));
        if (ancestorSynced) return;
        SyncMappings.Add(new CloudSyncMapping { CloudFolderId = cloudFolderId, LocalPath = localPath.Trim(), CloudFolderName = cloudFolderName ?? "" });
        _syncStore.Save(SyncMappings);
        _mappedSync.RestartFileWatchers();
        _ = LoadCurrentAsync();
    }

    public void RemoveMapping(Guid cloudFolderId)
    {
        var mapping = SyncMappings.FirstOrDefault(m => m.CloudFolderId == cloudFolderId);
        if (mapping == null) return;
        SyncMappings.Remove(mapping);
        _syncStore.Save(SyncMappings);
        _mappedSync.RestartFileWatchers();
        _ = LoadCurrentAsync();
    }

    public void RequestSyncForLocalPath(string? localPathOrFile) => _mappedSync.NotifyLocalPathChanged(localPathOrFile);

    public Task SyncMappingAsync(CloudSyncMapping mapping)
    {
        _mappedSync.StartManualSync(mapping);
        return Task.CompletedTask;
    }

    public async Task SyncAllMappingsOnStartupAsync()
    {
        if (!_authService.IsAuthenticated) return;
        var mappings = SyncMappings
            .Where(m => !string.IsNullOrWhiteSpace(m.LocalPath) && Directory.Exists(m.LocalPath))
            .ToList();
        var total = mappings.Count;
        for (var i = 0; i < mappings.Count; i++)
        {
            var m = mappings[i];
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                StatusText = total > 1 ? $"Syncing folder {i + 1}/{total}…" : "Syncing…");
            await _mappedSync.RunFolderSynchronizeAsync(m.CloudFolderId, m.LocalPath);
        }
    }

    public async Task EnsureRootAsync()
    {
        if (_authService.CurrentToken == null) return;
        var userId = _authService.CurrentToken.UserId;
        var list = await _api.GetEntitiesAsync(new[] { userId }, includeValues: false);
        var root = list?.FirstOrDefault(e => e.Code == CloudEntityCodes.CloudRoot);
        if (root != null)
        {
            CurrentFolderId = root.Id;
            BreadcrumbPath.Clear();
            BreadcrumbPath.Add(new CloudBreadcrumbEntry { Index = 0, Id = root.Id, Name = "Cloud" });
            await LoadCurrentAsync();
            return;
        }
        var newRootId = await _api.AddEntityAsync(CloudEntityCodes.CloudRoot, new[] { userId });
        CurrentFolderId = newRootId;
        BreadcrumbPath.Clear();
        BreadcrumbPath.Add(new CloudBreadcrumbEntry { Index = 0, Id = newRootId, Name = "Cloud" });
        await LoadCurrentAsync();
    }

    public async Task LoadCurrentAsync()
    {
        if (!CurrentFolderId.HasValue) return;
        SelectedItem = null;
        Items.Clear();
        StatusText = "Loading...";
        try
        {
            var list = await _api.GetEntitiesAsync(new[] { CurrentFolderId.Value }, includeValues: true);
            var syncedIds = new HashSet<Guid>(SyncMappings.Select(m => m.CloudFolderId));
            var items = (list ?? new List<GetEntitiesResult>())
                .Select(e => new CloudItemViewModel
                {
                    Id = e.Id,
                    Name = CloudApiService.GetDisplayName(e),
                    IsFolder = e.Code == CloudEntityCodes.CloudFolder,
                    IsSynced = e.Code == CloudEntityCodes.CloudFolder && syncedIds.Contains(e.Id)
                })
                .OrderByDescending(x => x.IsFolder)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);
            StatusText = "Ready";
        }
        catch (Exception ex)
        {
            StatusText = "Error: " + ex.Message;
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    public async Task NavigateToAsync(Guid folderId, string folderName)
    {
        if (BreadcrumbPath.Count > 0 && BreadcrumbPath[BreadcrumbPath.Count - 1].Id == folderId)
            return;
        BreadcrumbPath.Add(new CloudBreadcrumbEntry { Index = BreadcrumbPath.Count, Id = folderId, Name = folderName });
        CurrentFolderId = folderId;
        OnPropertyChanged(nameof(CanGoUp));
        await LoadCurrentAsync();
    }

    private void OpenItem(CloudItemViewModel item)
    {
        if (item.IsFolder)
            NavigateToFolder?.Invoke(item.Id, item.Name);
        else
            _ = DownloadAsync(item);
    }

    private void NewSubfolder(CloudItemViewModel parentFolder)
    {
        if (!parentFolder.IsFolder) return;
        if (!Dialogs.InputDialog.TryShow("Folder name:", "New subfolder", out var name) || string.IsNullOrWhiteSpace(name)) return;
        var nameTrim = name.Trim();
        var currentId = CurrentFolderId!.Value;
        var parentName = parentFolder.Name;
        var breadcrumbSnapshot = BreadcrumbPath.ToList();
        var mappingsSnapshot = SyncMappings.ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                if (CloudMappingMirrorPaths.TryGetSyncedMirrorForChildFolder(
                        currentId, parentName, breadcrumbSnapshot, mappingsSnapshot, out var root, out var parentLocal))
                {
                    Directory.CreateDirectory(Path.Combine(parentLocal, nameTrim));
                    await _mappedSync.RunFolderSynchronizeAsync(root.CloudFolderId, root.LocalPath);
                }
                else
                {
                    await _api.AddEntityAsync(CloudEntityCodes.CloudFolder, new[] { parentFolder.Id }, nameTrim);
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(LoadCurrentAsync);
            }
            catch (Exception ex)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => StatusText = "Error: " + ex.Message);
            }
        });
    }

    private void UploadHere(CloudItemViewModel targetFolder)
    {
        if (!targetFolder.IsFolder) return;
        var dlg = new Microsoft.Win32.OpenFileDialog();
        if (dlg.ShowDialog() != true) return;
        var path = dlg.FileName;
        var currentId = CurrentFolderId!.Value;
        var targetName = targetFolder.Name;
        var breadcrumbSnapshot = BreadcrumbPath.ToList();
        var mappingsSnapshot = SyncMappings.ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                var fileName = Path.GetFileName(path);
                if (CloudMappingMirrorPaths.TryGetSyncedMirrorForChildFolder(
                        currentId, targetName, breadcrumbSnapshot, mappingsSnapshot, out var root, out var targetLocal))
                {
                    Directory.CreateDirectory(targetLocal);
                    File.Copy(path, Path.Combine(targetLocal, fileName), overwrite: true);
                    await _mappedSync.RunFolderSynchronizeAsync(root.CloudFolderId, root.LocalPath);
                }
                else
                {
                    await UploadNewCloudFileAsync(targetFolder.Id, path);
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(LoadCurrentAsync);
            }
            catch (Exception ex)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => StatusText = "Error: " + ex.Message);
            }
        });
    }

    private void GoUp()
    {
        if (BreadcrumbPath.Count <= 1) return;
        BreadcrumbPath.RemoveAt(BreadcrumbPath.Count - 1);
        var last = BreadcrumbPath[BreadcrumbPath.Count - 1];
        CurrentFolderId = last.Id;
        for (var i = 0; i < BreadcrumbPath.Count; i++)
            BreadcrumbPath[i].Index = i;
        OnPropertyChanged(nameof(CanGoUp));
        _ = LoadCurrentAsync();
    }

    private void NavigateToBreadcrumb(int index)
    {
        if (index < 0 || index >= BreadcrumbPath.Count) return;
        while (BreadcrumbPath.Count > index + 1)
            BreadcrumbPath.RemoveAt(BreadcrumbPath.Count - 1);
        var entry = BreadcrumbPath[index];
        CurrentFolderId = entry.Id;
        for (var i = 0; i < BreadcrumbPath.Count; i++)
            BreadcrumbPath[i].Index = i;
        OnPropertyChanged(nameof(CanGoUp));
        _ = LoadCurrentAsync();
    }

    private void NewFolder()
    {
        if (!CurrentFolderId.HasValue) return;
        if (!Dialogs.InputDialog.TryShow("Folder name:", "New folder", out var name) || string.IsNullOrWhiteSpace(name)) return;
        var nameTrim = name.Trim();
        var currentId = CurrentFolderId.Value;
        var breadcrumbSnapshot = BreadcrumbPath.ToList();
        var mappingsSnapshot = SyncMappings.ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                if (CloudMappingMirrorPaths.TryGetSyncedMirror(currentId, breadcrumbSnapshot, mappingsSnapshot, out var root, out var localDir))
                {
                    Directory.CreateDirectory(Path.Combine(localDir, nameTrim));
                    await _mappedSync.RunFolderSynchronizeAsync(root.CloudFolderId, root.LocalPath);
                }
                else
                {
                    await _api.AddEntityAsync(CloudEntityCodes.CloudFolder, new[] { currentId }, nameTrim);
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(LoadCurrentAsync);
            }
            catch (Exception ex)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => StatusText = "Error: " + ex.Message);
            }
        });
    }

    private void Upload()
    {
        if (!CurrentFolderId.HasValue) return;
        var dlg = new Microsoft.Win32.OpenFileDialog();
        if (dlg.ShowDialog() != true) return;
        var path = dlg.FileName;
        var currentId = CurrentFolderId.Value;
        var breadcrumbSnapshot = BreadcrumbPath.ToList();
        var mappingsSnapshot = SyncMappings.ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                var fileName = Path.GetFileName(path);
                if (CloudMappingMirrorPaths.TryGetSyncedMirror(currentId, breadcrumbSnapshot, mappingsSnapshot, out var root, out var localDir))
                {
                    Directory.CreateDirectory(localDir);
                    File.Copy(path, Path.Combine(localDir, fileName), overwrite: true);
                    await _mappedSync.RunFolderSynchronizeAsync(root.CloudFolderId, root.LocalPath);
                }
                else
                {
                    await UploadNewCloudFileAsync(currentId, path);
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(LoadCurrentAsync);
            }
            catch (Exception ex)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => StatusText = "Error: " + ex.Message);
            }
        });
    }

    private async Task UploadNewCloudFileAsync(Guid parentFolderId, string localFilePath)
    {
        var fileName = Path.GetFileName(localFilePath);
        var entityId = await _api.AddEntityAsync(CloudEntityCodes.CloudFile, new[] { parentFolderId }, fileName);
        await using var stream = File.OpenRead(localFilePath);
        var mime = MimeTypes.GetMimeType(fileName);
        await _api.AddFileAsync(entityId, fileName, stream, mime);
    }

    private async Task DeleteAsync(CloudItemViewModel item)
    {
        try
        {
            var localMirror = CloudMappingMirrorPaths.GetLocalPathForCloudItem(item, BreadcrumbPath, SyncMappings);
            _mappedSync.SuspendMappedRootWatchers();
            try
            {
                if (item.IsFolder)
                {
                    var children = await _api.GetEntitiesAsync(new[] { item.Id }, includeValues: false);
                    foreach (var c in children ?? new List<GetEntitiesResult>())
                    {
                        if (c.Code == CloudEntityCodes.CloudFile)
                            await _api.DeleteEntityAsync(c.Id);
                        else if (c.Code == CloudEntityCodes.CloudFolder)
                            await DeleteFolderRecursiveAsync(c.Id);
                    }
                    await _api.DeleteEntityAsync(item.Id);
                }
                else
                {
                    await _api.DeleteEntityAsync(item.Id);
                }

                CloudMappingMirrorPaths.DeleteLocalMirror(item, localMirror);
                Items.Remove(item);
            }
            finally
            {
                _mappedSync.ResumeMappedRootWatchers();
            }

            await LoadCurrentAsync();
        }
        catch (Exception ex)
        {
            StatusText = "Error: " + ex.Message;
        }
    }

    private async Task DeleteFolderRecursiveAsync(Guid folderId)
    {
        var children = await _api.GetEntitiesAsync(new[] { folderId }, includeValues: false);
        foreach (var c in children ?? new List<GetEntitiesResult>())
        {
            if (c.Code == CloudEntityCodes.CloudFile)
                await _api.DeleteEntityAsync(c.Id);
            else
                await DeleteFolderRecursiveAsync(c.Id);
        }
        await _api.DeleteEntityAsync(folderId);
    }

    public async Task DownloadAsync(CloudItemViewModel item)
    {
        if (item.IsFolder) { NavigateToFolder?.Invoke(item.Id, item.Name); return; }
        try
        {
            var stream = await _api.GetFileStreamAsync(item.Id);
            DownloadFile?.Invoke(item.Name, stream);
        }
        catch (Exception ex)
        {
            StatusText = "Error: " + ex.Message;
        }
    }

    private void OpenWebExplorerInBrowser()
    {
        if (!_currentFolderId.HasValue) return;
        var baseUrl = (_settingsService.LoadSettings().PublicSiteBaseUrl ?? "https://protolink.ru/").TrimEnd('/');
        var url = $"{baseUrl}/{_currentFolderId.Value:D}?lang=ru-RU";
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = "Could not open browser: " + ex.Message;
        }
    }
}
