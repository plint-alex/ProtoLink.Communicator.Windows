using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Windows.Input;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
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
    private readonly Action? _onAuthRequired;
    private readonly NavigationHistory<Guid> _history = new();
    private readonly Dictionary<Guid, CloudLocation> _locations = new();
    private CloudEntityRef? _currentEntity;
    private CloudItemViewModel? _selectedItem;
    private string _statusText = "Ready";

    public CloudViewModel(
        IAuthService authService,
        HttpClient httpClient,
        ISyncMappingStore syncStore,
        ISettingsService settingsService,
        Action<Exception>? syncFullErrorReporter = null,
        Action? onAuthRequired = null,
        Action? onAfterMappedSync = null)
    {
        _authService = authService;
        _settingsService = settingsService;
        _api = new CloudApiService(httpClient);
        var folderSynchronizer = new CloudFolderSynchronizer(_api);
        _syncStore = syncStore;
        _syncFullErrorReporter = syncFullErrorReporter;
        _onAuthRequired = onAuthRequired;
        Items = new ObservableCollection<CloudItemViewModel>();
        BreadcrumbPath = new ObservableCollection<CloudBreadcrumbEntry>();
        SyncMappings = new ObservableCollection<CloudSyncMapping>(_syncStore.Load());
        _mappedSync = new CloudMappedSyncCoordinator(
            folderSynchronizer,
            _api,
            SyncMappings,
            () => LoadCurrentAsync(quiet: true),
            s => StatusText = s,
            ex =>
            {
                if (CloudFolderSynchronizer.IsAuthFailure(ex))
                {
                    StatusText = "Please log in.";
                    _onAuthRequired?.Invoke();
                    return;
                }
                _syncFullErrorReporter?.Invoke(ex);
            },
            System.Windows.Application.Current.Dispatcher,
            () => _authService.IsAuthenticated,
            () => _authService.CurrentToken?.UserId.ToString(),
            () => _onAuthRequired?.Invoke(),
            onSyncCompleted: () =>
            {
                try { onAfterMappedSync?.Invoke(); } catch { /* ignore */ }
            });
        RefreshCommand = new RelayCommand(async _ => await LoadCurrentAsync());
        NewFolderCommand = new RelayCommand(_ => NewFolder(), _ => ListedFolderId.HasValue);
        UploadCommand = new RelayCommand(_ => Upload(), _ => ListedFolderId.HasValue);
        DeleteCommand = new RelayCommand(async p => await DeleteAsync((CloudItemViewModel)p!), p => p is CloudItemViewModel);
        BackCommand = new RelayCommand(async _ => await GoBackAsync(), _ => _history.CanGoBack);
        NavigateToBreadcrumbCommand = new RelayCommand(async p => await NavigateToBreadcrumbAsync((int)p!));
        OpenCommand = new RelayCommand(async p => await OpenItemAsync((CloudItemViewModel)p!), p => p is CloudItemViewModel);
        NewSubfolderCommand = new RelayCommand(p => NewSubfolder((CloudItemViewModel)p!), p => p is CloudItemViewModel item && item.IsFolder);
        UploadHereCommand = new RelayCommand(p => UploadHere((CloudItemViewModel)p!), p => p is CloudItemViewModel item && item.IsFolder);
        DownloadCommand = new RelayCommand(
            _ => { if (SelectedItem != null && !SelectedItem.IsFolder) _ = DownloadAsync(SelectedItem); },
            _ => SelectedItem != null && !SelectedItem.IsFolder);
        CopyLinkCommand = new RelayCommand(p => CopyLink(LinkTargetId(p)), p => LinkTargetId(p).HasValue);
        OpenInBrowserCommand = new RelayCommand(p => OpenInBrowser(LinkTargetId(p)), p => LinkTargetId(p).HasValue);
        _history.Changed += CommandManager.InvalidateRequerySuggested;
    }

    public ObservableCollection<CloudItemViewModel> Items { get; }
    public ObservableCollection<CloudBreadcrumbEntry> BreadcrumbPath { get; }
    public ObservableCollection<CloudSyncMapping> SyncMappings { get; }
    public bool IsEmpty => Items.Count == 0;

    /// <summary>Open entity (folder or file). Not the same as <see cref="SelectedItem"/>.</summary>
    public CloudEntityRef? CurrentEntity
    {
        get => _currentEntity;
        private set
        {
            _currentEntity = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Folder whose children the list shows: the open folder, or the open file's parent.</summary>
    public Guid? ListedFolderId => CurrentEntity is null ? null : CurrentEntity.IsFolder ? CurrentEntity.Id : CurrentEntity.ParentId;

    /// <summary>Written by the view on scroll; stored in history so Back restores it.</summary>
    public double ListScrollOffset { get; set; }
    public event Action<double>? ListScrollRestoreRequested;

    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public ICommand RefreshCommand { get; }
    public ICommand NewFolderCommand { get; }
    public ICommand UploadCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand NavigateToBreadcrumbCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand NewSubfolderCommand { get; }
    public ICommand UploadHereCommand { get; }
    public ICommand DownloadCommand { get; }
    /// <summary>Parameter: a row (<see cref="CloudItemViewModel"/>) or null for <see cref="CurrentEntity"/>.</summary>
    public ICommand CopyLinkCommand { get; }
    /// <summary>Parameter: a row (<see cref="CloudItemViewModel"/>) or null for <see cref="CurrentEntity"/>.</summary>
    public ICommand OpenInBrowserCommand { get; }

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
        StatusText = "Mapped — syncing into existing cloud folder…";
        _ = LoadCurrentAsync();
        // Full reconcile into the existing CloudFolderId — never create a new root folder for the map.
        _ = _mappedSync.RequestFullSyncAsync();
    }

    public void RemoveMapping(Guid cloudFolderId)
    {
        var mapping = SyncMappings.FirstOrDefault(m => m.CloudFolderId == cloudFolderId);
        if (mapping == null) return;
        SyncMappings.Remove(mapping);
        _syncStore.Save(SyncMappings);
        _ = LoadCurrentAsync();
    }

    /// <summary>Refresh cloud folder listing without clearing selection or flashing empty.</summary>
    public async Task RefreshFromRealtimeAsync()
    {
        if (!_authService.IsAuthenticated) return;
        if (ListedFolderId.HasValue)
            await LoadCurrentAsync(quiet: true);
    }

    /// <summary>Full reconcile from SignalR <c>data_changed</c> (or coalesced after busy sync).</summary>
    public Task RequestFullSyncFromRealtimeAsync() => _mappedSync.RequestFullSyncAsync();

    /// <summary>Local-only push after Notes page save (same as interval).</summary>
    public void RequestLocalPushAfterNoteSave() => _ = _mappedSync.RequestLocalPushAsync();

    public void StopAutoSync() => _mappedSync.StopAutoSyncInterval();

    public Task SyncMappingAsync(CloudSyncMapping mapping)
    {
        _mappedSync.StartManualSync(mapping);
        return Task.CompletedTask;
    }

    public Task ForceUploadMappingAsync(CloudSyncMapping mapping)
    {
        var confirm = System.Windows.MessageBox.Show(
            $"Upload all local files in:\n{mapping.LocalPath}\n\nto the server (overwrite remote)?",
            "Force upload",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes)
            return Task.CompletedTask;
        _mappedSync.StartForcePush(mapping);
        return Task.CompletedTask;
    }

    public Task ForceDownloadMappingAsync(CloudSyncMapping mapping)
    {
        var confirm = System.Windows.MessageBox.Show(
            $"Download all server files for “{mapping.CloudFolderName}”\n\noverwriting local files in:\n{mapping.LocalPath}?",
            "Force download",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes)
            return Task.CompletedTask;
        _mappedSync.StartForcePull(mapping);
        return Task.CompletedTask;
    }

    public async Task SyncAllMappingsOnStartupAsync()
    {
        if (!_authService.IsAuthenticated)
        {
            StatusText = "Please log in.";
            return;
        }
        var mappings = SyncMappings
            .Where(m => !string.IsNullOrWhiteSpace(m.LocalPath) && Directory.Exists(m.LocalPath))
            .ToList();
        if (mappings.Count == 0) return;
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => StatusText = "Syncing…");
        await _mappedSync.SynchronizeAllMappedAsync();
    }

    public async Task EnsureRootAsync()
    {
        if (_authService.CurrentToken == null) return;
        var userId = _authService.CurrentToken.UserId;
        var list = await _api.GetEntitiesAsync(new[] { userId }, includeValues: false);
        var rootId = list?.FirstOrDefault(e => e.Code == CloudEntityCodes.CloudRoot)?.Id
            ?? await _api.AddEntityAsync(CloudEntityCodes.CloudRoot, new[] { userId });
        _history.Clear();
        _locations.Clear();
        await OpenAsync(
            new CloudEntityRef(rootId, CloudEntityKind.Folder, "Cloud", null),
            new[] { new CloudBreadcrumbEntry { Id = rootId, Name = "Cloud" } },
            pushHistory: false);
    }

    private Guid? _listedFolderId;

    public async Task LoadCurrentAsync(bool quiet = true, Guid? restoreSelectionId = null)
    {
        if (ListedFolderId is not { } folderId) return;
        var folderChanged = _listedFolderId != folderId;
        if (folderChanged)
            SelectedItem = null;
        var selectedId = restoreSelectionId ?? SelectedItem?.Id;

        if (!quiet)
            StatusText = "Loading...";
        try
        {
            var list = await _api.GetEntitiesAsync(new[] { folderId }, includeValues: true);
            // User may have navigated away while the request was in flight.
            if (ListedFolderId != folderId) return;

            var syncedIds = new HashSet<Guid>(SyncMappings.Select(m => m.CloudFolderId));
            var fresh = (list ?? new List<GetEntitiesResult>())
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

            MergeListing(fresh, selectedId, reset: folderChanged);
            _listedFolderId = folderId;
            if (!quiet)
                StatusText = "Ready";
        }
        catch (Exception ex)
        {
            ReportCaughtException(ex);
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// Update <see cref="Items"/> in place so ListBox does not flash empty and selection stays on the same row.
    /// </summary>
    private void MergeListing(List<CloudItemViewModel> fresh, Guid? selectedId, bool reset)
    {
        if (reset)
            Items.Clear();

        var freshIds = new HashSet<Guid>(fresh.Select(f => f.Id));
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!freshIds.Contains(Items[i].Id))
                Items.RemoveAt(i);
        }

        for (var target = 0; target < fresh.Count; target++)
        {
            var want = fresh[target];
            var currentIndex = -1;
            for (var i = 0; i < Items.Count; i++)
            {
                if (Items[i].Id == want.Id)
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                Items.Insert(target, want);
                continue;
            }

            var existing = Items[currentIndex];
            existing.Name = want.Name;
            existing.IsFolder = want.IsFolder;
            existing.IsSynced = want.IsSynced;
            if (currentIndex != target)
            {
                Items.RemoveAt(currentIndex);
                Items.Insert(target, existing);
            }
        }

        if (selectedId.HasValue)
        {
            var sel = Items.FirstOrDefault(i => i.Id == selectedId.Value);
            if (!ReferenceEquals(SelectedItem, sel))
                SelectedItem = sel;
        }
    }

    private void ReportCaughtException(Exception ex)
    {
        if (CloudFolderSynchronizer.IsAuthFailure(ex))
        {
            StatusText = "Please log in.";
            _onAuthRequired?.Invoke();
        }
        else
            StatusText = "Error: " + ex.Message;
    }

    /// <summary>Breadcrumb trail of the listed folder at the time an entity was opened; Back restores it.</summary>
    private sealed record CloudLocation(CloudEntityRef Entity, IReadOnlyList<CloudBreadcrumbEntry> Breadcrumb);

    private async Task OpenAsync(
        CloudEntityRef target,
        IEnumerable<CloudBreadcrumbEntry> breadcrumb,
        bool pushHistory = true,
        Guid? restoreSelectionId = null,
        double? restoreScrollOffset = null)
    {
        if (pushHistory && CurrentEntity != null)
            _history.Push(new NavigationEntry<Guid>(CurrentEntity.Id, ListScrollOffset, SelectedItem?.Id ?? Guid.Empty));

        var trail = breadcrumb.Select((b, i) => new CloudBreadcrumbEntry { Index = i, Id = b.Id, Name = b.Name }).ToList();
        _locations[target.Id] = new CloudLocation(target, trail);
        if (!BreadcrumbPath.Select(b => b.Id).SequenceEqual(trail.Select(b => b.Id)))
        {
            BreadcrumbPath.Clear();
            foreach (var b in trail)
                BreadcrumbPath.Add(new CloudBreadcrumbEntry { Index = b.Index, Id = b.Id, Name = b.Name });
        }

        CurrentEntity = target;
        await LoadCurrentAsync(quiet: true, restoreSelectionId);
        if (restoreScrollOffset is { } offset)
            ListScrollRestoreRequested?.Invoke(offset);
    }

    private async Task OpenItemAsync(CloudItemViewModel item)
    {
        if (ListedFolderId is not { } parentId || CurrentEntity?.Id == item.Id) return;
        var kind = item.IsFolder ? CloudEntityKind.Folder : CloudEntityKind.File;
        var trail = item.IsFolder
            ? BreadcrumbPath.Append(new CloudBreadcrumbEntry { Id = item.Id, Name = item.Name })
            : BreadcrumbPath;
        await OpenAsync(new CloudEntityRef(item.Id, kind, item.Name, parentId), trail.ToList());
    }

    private async Task NavigateToBreadcrumbAsync(int index)
    {
        if (index < 0 || index >= BreadcrumbPath.Count) return;
        var entry = BreadcrumbPath[index];
        if (CurrentEntity?.Id == entry.Id) return;
        var parentId = index > 0 ? BreadcrumbPath[index - 1].Id : (Guid?)null;
        await OpenAsync(
            new CloudEntityRef(entry.Id, CloudEntityKind.Folder, entry.Name, parentId),
            BreadcrumbPath.Take(index + 1).ToList());
    }

    private async Task GoBackAsync()
    {
        while (_history.TryPop(out var entry))
        {
            if (!_locations.TryGetValue(entry.Key, out var location)) continue;
            await OpenAsync(
                location.Entity,
                location.Breadcrumb,
                pushHistory: false,
                restoreSelectionId: entry.SelectedKey == Guid.Empty ? null : entry.SelectedKey,
                restoreScrollOffset: entry.ListScrollOffset);
            return;
        }
    }

    private void NewSubfolder(CloudItemViewModel parentFolder)
    {
        if (!parentFolder.IsFolder) return;
        if (!Dialogs.InputDialog.TryShow("Folder name:", "New subfolder", out var name) || string.IsNullOrWhiteSpace(name)) return;
        var nameTrim = name.Trim();
        var currentId = ListedFolderId!.Value;
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

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => LoadCurrentAsync(quiet: true));
            }
            catch (Exception ex)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => ReportCaughtException(ex));
            }
        });
    }

    private void UploadHere(CloudItemViewModel targetFolder)
    {
        if (!targetFolder.IsFolder) return;
        var dlg = new Microsoft.Win32.OpenFileDialog();
        if (dlg.ShowDialog() != true) return;
        var path = dlg.FileName;
        var currentId = ListedFolderId!.Value;
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

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => LoadCurrentAsync(quiet: true));
            }
            catch (Exception ex)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => ReportCaughtException(ex));
            }
        });
    }

    private void NewFolder()
    {
        if (ListedFolderId is not { } currentId) return;
        if (!Dialogs.InputDialog.TryShow("Folder name:", "New folder", out var name) || string.IsNullOrWhiteSpace(name)) return;
        var nameTrim = name.Trim();
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

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => LoadCurrentAsync(quiet: true));
            }
            catch (Exception ex)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => ReportCaughtException(ex));
            }
        });
    }

    private void Upload()
    {
        if (ListedFolderId is not { } currentId) return;
        var dlg = new Microsoft.Win32.OpenFileDialog();
        if (dlg.ShowDialog() != true) return;
        var path = dlg.FileName;
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

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => LoadCurrentAsync(quiet: true));
            }
            catch (Exception ex)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => ReportCaughtException(ex));
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
                _locations.Remove(item.Id);
                if (CurrentEntity?.Id == item.Id && ListedFolderId is { } listedId && _locations.TryGetValue(listedId, out var folder))
                    CurrentEntity = folder.Entity;
            }
            finally
            {
                _mappedSync.ResumeMappedRootWatchers();
            }

            await LoadCurrentAsync();
        }
        catch (Exception ex)
        {
            ReportCaughtException(ex);
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
        if (item.IsFolder) return;
        try
        {
            var stream = await _api.GetFileStreamAsync(item.Id);
            DownloadFile?.Invoke(item.Name, stream);
        }
        catch (Exception ex)
        {
            ReportCaughtException(ex);
        }
    }

    private Guid? LinkTargetId(object? parameter) => parameter is CloudItemViewModel item ? item.Id : CurrentEntity?.Id;

    private string BuildLink(Guid entityId)
    {
        var settings = _settingsService.LoadSettings();
        return EntityLinks.Build(settings.PublicSiteBaseUrl, entityId, settings.UiLanguage);
    }

    private void CopyLink(Guid? entityId)
    {
        if (entityId is not { } id) return;
        try
        {
            System.Windows.Clipboard.SetText(BuildLink(id));
            StatusText = "Link copied";
        }
        catch (Exception ex)
        {
            StatusText = "Could not copy link: " + ex.Message;
        }
    }

    private void OpenInBrowser(Guid? entityId)
    {
        if (entityId is not { } id) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = BuildLink(id), UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = "Could not open browser: " + ex.Message;
        }
    }
}
