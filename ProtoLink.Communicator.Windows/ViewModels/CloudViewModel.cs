using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows.Data;
using System.Windows.Input;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels.Share;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class CloudViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly ISettingsService _settingsService;
    private readonly CloudApiService _api;
    private readonly CloudMappedSyncCoordinator _mappedSync;
    private readonly ISyncMappingStore _syncStore;
    private readonly Action<Exception>? _syncFullErrorReporter;
    private readonly Action? _onAuthRequired;
    private readonly Action<string>? _onSendShareLinkInMessenger;
    private readonly NavigationHistory<Guid> _history = new();
    private readonly Dictionary<Guid, CloudLocation> _locations = new();
    private CloudEntityRef? _currentEntity;
    private CloudItemViewModel? _selectedItem;
    private string _statusText = "Ready";
    private string _searchText = "";
    private bool _showNetworkBanner;
    private Exception? _lastError;
    private bool _isCompactLayout;
    private bool _showEntityInCompact;
    private Guid? _listedFolderId;
    private Guid _myCloudRootId;
    private readonly Dictionary<Guid, string> _shareRootCaptions = new();
    /// <summary>CanWrite from SharedWithMe listing; survives navigation into share-root descendants.</summary>
    private readonly Dictionary<Guid, bool> _shareRootCanWrite = new();

    public CloudViewModel(
        IAuthService authService,
        HttpClient httpClient,
        ISyncMappingStore syncStore,
        ISettingsService settingsService,
        Action<Exception>? syncFullErrorReporter = null,
        Action? onAuthRequired = null,
        Action? onAfterMappedSync = null,
        Action<string>? onSendShareLinkInMessenger = null)
    {
        _authService = authService;
        _settingsService = settingsService;
        _api = new CloudApiService(httpClient);
        var folderSynchronizer = new CloudFolderSynchronizer(_api);
        _syncStore = syncStore;
        _syncFullErrorReporter = syncFullErrorReporter;
        _onAuthRequired = onAuthRequired;
        _onSendShareLinkInMessenger = onSendShareLinkInMessenger;
        Items = new ObservableCollection<CloudItemViewModel>();
        FilteredItems = CollectionViewSource.GetDefaultView(Items);
        FilteredItems.Filter = MatchesSearchFilter;
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
        ShareSheet = new ShareSheetViewModel(
            _api,
            () => _settingsService.LoadSettings().UiLanguage,
            () => _authService.CurrentToken?.UserId,
            s => StatusText = s,
            url => _onSendShareLinkInMessenger?.Invoke(url));
        ShareSheet.CopyCanonicalLink = () => CopyLink(CurrentEntity?.Id);
        ShareSheet.GetCanonicalLink = () => CurrentEntity?.Id is { } id ? BuildLink(id) : null;
        RefreshCommand = new RelayCommand(async _ => await LoadCurrentAsync());
        NewFolderCommand = new RelayCommand(_ => NewFolder(), _ => CanMutateListedFolder);
        UploadCommand = new RelayCommand(_ => Upload(), _ => CanMutateListedFolder);
        DeleteCommand = new RelayCommand(
            async p => await DeleteAsync((CloudItemViewModel)p!),
            p => p is CloudItemViewModel item && !item.IsVirtualEntry && !CloudVirtualNodes.IsVirtual(item.Id));
        BackCommand = new RelayCommand(async _ => await HandleBackAsync(), _ => CanGoBack);
        NavigateToBreadcrumbCommand = new RelayCommand(async p => await NavigateToBreadcrumbAsync((int)p!));
        NavigateParentCommand = new RelayCommand(async _ => await NavigateToParentAsync(), _ => CanNavigateParent);
        OpenCommand = new RelayCommand(async p => await OpenItemAsync((CloudItemViewModel)p!), p => p is CloudItemViewModel);
        NewSubfolderCommand = new RelayCommand(
            p => NewSubfolder((CloudItemViewModel)p!),
            p => p is CloudItemViewModel item && item.IsFolder && !item.IsVirtualEntry && !CloudVirtualNodes.IsVirtual(item.Id));
        UploadHereCommand = new RelayCommand(
            p => UploadHere((CloudItemViewModel)p!),
            p => p is CloudItemViewModel item && item.IsFolder && !item.IsVirtualEntry && !CloudVirtualNodes.IsVirtual(item.Id));
        DownloadCommand = new RelayCommand(
            _ => { if (SelectedItem != null && !SelectedItem.IsFolder) _ = DownloadAsync(SelectedItem); },
            _ => SelectedItem != null && !SelectedItem.IsFolder);
        DownloadCurrentCommand = new RelayCommand(
            _ =>
            {
                if (CurrentEntity is { IsFolder: false } file)
                    _ = DownloadEntityAsync(file);
            },
            _ => CurrentEntity is { IsFolder: false });
        InitPreviewCommands();
        DeleteCurrentCommand = new RelayCommand(
            _ =>
            {
                if (CurrentEntity is null) return;
                var item = Items.FirstOrDefault(i => i.Id == CurrentEntity.Id)
                    ?? new CloudItemViewModel { Id = CurrentEntity.Id, Name = CurrentEntity.Name, IsFolder = CurrentEntity.IsFolder };
                _ = DeleteAsync(item);
            },
            _ => CurrentEntity is { ParentId: not null });
        CopyLinkCommand = new RelayCommand(p => CopyLink(LinkTargetId(p)), p => LinkTargetId(p).HasValue);
        OpenInBrowserCommand = new RelayCommand(p => OpenInBrowser(LinkTargetId(p)), p => LinkTargetId(p).HasValue);
        OpenShareCommand = new RelayCommand(
            async p => await OpenShareAsync(ShareTargetId(p)),
            p => ShareTargetId(p).HasValue);
        SyncCurrentMappingCommand = new RelayCommand(_ => SyncCurrentMapping(), _ => HasCurrentFolderMapping);
        ForceUploadCurrentMappingCommand = new RelayCommand(_ => ForceUploadCurrentMapping(), _ => HasCurrentFolderMapping);
        ForceDownloadCurrentMappingCommand = new RelayCommand(_ => ForceDownloadCurrentMapping(), _ => HasCurrentFolderMapping);
        UnmapCurrentFolderCommand = new RelayCommand(_ => UnmapCurrentFolder(), _ => HasCurrentFolderMapping);
        ShowErrorDetailsCommand = new RelayCommand(_ => ShowErrorDetails(), _ => HasErrorDetails);
        _history.Changed += () =>
        {
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(ShowBack));
            CommandManager.InvalidateRequerySuggested();
        };
    }

    public ObservableCollection<CloudItemViewModel> Items { get; }
    public ICollectionView FilteredItems { get; }
    public ObservableCollection<CloudBreadcrumbEntry> BreadcrumbPath { get; }
    public ObservableCollection<CloudSyncMapping> SyncMappings { get; }
    public bool IsEmpty => Items.Count == 0;

    public bool IsSharedWithMeListing =>
        CurrentEntity?.Id == CloudVirtualNodes.SharedWithMeId;

    public bool IsCloudHubListing =>
        CurrentEntity?.Id == CloudVirtualNodes.HubId;

    /// <summary>Real Cloud folders only — not hub / Shared with me virtual nodes.</summary>
    public bool CanMutateListedFolder =>
        ListedFolderId is { } id
        && !CloudVirtualNodes.IsVirtual(id);

    public bool ShowEmptyUpload => IsEmpty && CanMutateListedFolder;

    public string EmptyListTitle
    {
        get
        {
            var lang = _settingsService.LoadSettings().UiLanguage;
            return IsSharedWithMeListing
                ? ShareSheetStrings.EmptySharedTitle(lang)
                : ShareSheetStrings.EmptyFolderTitle(lang);
        }
    }

    public string EmptyListBody
    {
        get
        {
            var lang = _settingsService.LoadSettings().UiLanguage;
            return IsSharedWithMeListing
                ? ShareSheetStrings.EmptySharedBody(lang)
                : ShareSheetStrings.EmptyFolderBody(lang);
        }
    }

    public string EmptyListGlyph => IsSharedWithMeListing ? "👥" : "📁";

    /// <summary>Open entity (folder or file). Not the same as <see cref="SelectedItem"/>.</summary>
    public CloudEntityRef? CurrentEntity
    {
        get => _currentEntity;
        private set
        {
            _currentEntity = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ListedFolderId));
            NotifyHeaderAndInfoChanged();
            BeginFilePreview();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Folder whose children the list shows: the open folder, or the open file's parent.</summary>
    public Guid? ListedFolderId => CurrentEntity is null ? null : CurrentEntity.IsFolder ? CurrentEntity.Id : CurrentEntity.ParentId;

    /// <summary>Written by the view on scroll; stored in history so Back restores it.</summary>
    public double ListScrollOffset { get; set; }
    public event Action<double>? ListScrollRestoreRequested;
    /// <summary>Raised after an entity is opened so the view can show the entity pane in compact layout.</summary>
    public event Action? EntityOpened;

    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowStatusBanner));
        }
    }

    /// <summary>AT-C6: shown when a folder listing fails; cleared on successful load / SignalR reconnect refresh.</summary>
    public bool ShowNetworkBanner => _showNetworkBanner;

    /// <summary>Transient sync/copy/error line (replaces the permanent status bar).</summary>
    public bool ShowStatusBanner =>
        !_showNetworkBanner
        && !string.IsNullOrWhiteSpace(_statusText)
        && !string.Equals(_statusText, "Ready", StringComparison.Ordinal);

    public bool HasErrorDetails => _lastError != null;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            FilteredItems.Refresh();
        }
    }

    public bool IsCompactLayout
    {
        get => _isCompactLayout;
        set
        {
            if (_isCompactLayout == value) return;
            _isCompactLayout = value;
            if (!_isCompactLayout)
                _showEntityInCompact = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowEntityPane));
            OnPropertyChanged(nameof(ShowListPane));
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(ShowBack));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool ShowEntityInCompact
    {
        get => _showEntityInCompact;
        set
        {
            if (_showEntityInCompact == value) return;
            _showEntityInCompact = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowEntityPane));
            OnPropertyChanged(nameof(ShowListPane));
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(ShowBack));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool ShowListPane => !IsCompactLayout || !ShowEntityInCompact;
    public bool ShowEntityPane => !IsCompactLayout || ShowEntityInCompact;

    public bool CanGoBack => _history.CanGoBack || (IsCompactLayout && ShowEntityInCompact);
    public bool ShowBack => CanGoBack;

    public string HeaderTitle => CurrentEntity?.Name ?? ShareSheetStrings.CloudHub(_settingsService.LoadSettings().UiLanguage);
    public string HeaderTypeGlyph =>
        CurrentEntity is null ? ""
        : CurrentEntity.Id == CloudVirtualNodes.SharedWithMeId ? "👥"
        : CurrentEntity.IsFolder ? "📁" : "📄";
    public string HeaderSubtitle
    {
        get
        {
            if (CurrentEntity is { } ent && _shareRootCaptions.TryGetValue(ent.Id, out var caption))
                return caption;
            if (CurrentEntity?.ParentId is null || BreadcrumbPath.Count < 2) return string.Empty;
            var parent = BreadcrumbPath[^2];
            return "… › " + parent.Name;
        }
    }

    public bool CanNavigateParent => BreadcrumbPath.Count >= 2;
    public bool IsFolderOpen => CurrentEntity?.IsFolder == true;
    public bool IsFileOpen => CurrentEntity is { IsFolder: false };
    /// <summary>Entity id for the Info panel (AT-S40); empty when nothing is open.</summary>
    public string CurrentEntityIdText => CurrentEntity is { } e ? e.Id.ToString("D") : "";
    public bool HasCurrentEntityId =>
        CurrentEntity != null && !CloudVirtualNodes.IsVirtual(CurrentEntity.Id);
    public int ListedChildCount => Items.Count;
    public string ListedChildCountText => ListedChildCount == 1 ? "1 item" : $"{ListedChildCount} items";
    public string? FolderSizeSummary
    {
        get
        {
            if (CurrentFolderMapping?.LocalPath is not { } path || !Directory.Exists(path)) return null;
            try
            {
                var bytes = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
                return FormatByteSize(bytes);
            }
            catch
            {
                return null;
            }
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand NewFolderCommand { get; }
    public ICommand UploadCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand DeleteCurrentCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand NavigateToBreadcrumbCommand { get; }
    public ICommand NavigateParentCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand NewSubfolderCommand { get; }
    public ICommand UploadHereCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand DownloadCurrentCommand { get; }
    /// <summary>Parameter: a row (<see cref="CloudItemViewModel"/>) or null for <see cref="CurrentEntity"/>.</summary>
    public ICommand CopyLinkCommand { get; }
    /// <summary>Parameter: a row (<see cref="CloudItemViewModel"/>) or null for <see cref="CurrentEntity"/>.</summary>
    public ICommand OpenInBrowserCommand { get; }
    /// <summary>Opens the Share side sheet for current entity or a row parameter.</summary>
    public ICommand OpenShareCommand { get; }
    public ShareSheetViewModel ShareSheet { get; }
    public string ShareButtonLabel =>
        ShareSheetStrings.ShareButton(_settingsService.LoadSettings().UiLanguage);
    public ICommand SyncCurrentMappingCommand { get; }
    public ICommand ForceUploadCurrentMappingCommand { get; }
    public ICommand ForceDownloadCurrentMappingCommand { get; }
    public ICommand UnmapCurrentFolderCommand { get; }
    public ICommand ShowErrorDetailsCommand { get; }

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

    public void ReturnToListInCompact()
    {
        if (IsCompactLayout)
            ShowEntityInCompact = false;
    }

    /// <summary>Esc: close Share sheet, or leave entity focus in compact layout; caller focuses the list.</summary>
    public bool HandleEscape()
    {
        if (ShareSheet.IsOpen)
        {
            ShareSheet.CloseCommand.Execute(null);
            return true;
        }

        if (IsCompactLayout && ShowEntityInCompact)
        {
            ShowEntityInCompact = false;
            return true;
        }

        return false;
    }

    private bool MatchesSearchFilter(object obj)
    {
        if (obj is not CloudItemViewModel item) return false;
        if (string.IsNullOrWhiteSpace(_searchText)) return true;
        return item.Name.Contains(_searchText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task HandleBackAsync()
    {
        if (_history.CanGoBack)
        {
            await GoBackAsync();
            return;
        }

        if (IsCompactLayout && ShowEntityInCompact)
            ShowEntityInCompact = false;
    }

    private void NotifyHeaderAndInfoChanged()
    {
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(HeaderTypeGlyph));
        OnPropertyChanged(nameof(CanNavigateParent));
        OnPropertyChanged(nameof(IsFolderOpen));
        OnPropertyChanged(nameof(IsFileOpen));
        OnPropertyChanged(nameof(CurrentEntityIdText));
        OnPropertyChanged(nameof(HasCurrentEntityId));
        OnPropertyChanged(nameof(ListedChildCount));
        OnPropertyChanged(nameof(ListedChildCountText));
        OnPropertyChanged(nameof(CurrentFolderMapping));
        OnPropertyChanged(nameof(HasCurrentFolderMapping));
        OnPropertyChanged(nameof(CurrentFolderMappingPath));
        OnPropertyChanged(nameof(FolderSizeSummary));
        OnPropertyChanged(nameof(CanMapCurrentFolder));
        OnPropertyChanged(nameof(IsInSharedWithMeScope));
        OnPropertyChanged(nameof(ShowSharedBrowseOnlyHint));
        OnPropertyChanged(nameof(SharedBrowseOnlyHintText));
        OnPropertyChanged(nameof(ShareButtonLabel));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(ShowBack));
    }

    private static string FormatByteSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} {units[unit]}" : $"{size:0.#} {units[unit]}";
    }

    /// <summary>Refresh cloud folder listing without clearing selection or flashing empty. Clears offline banner on success.</summary>
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

    public async Task EnsureRootAsync()
    {
        if (_authService.CurrentToken == null) return;
        var userId = _authService.CurrentToken.UserId;
        var list = await _api.GetEntitiesAsync(new[] { userId }, includeValues: false);
        _myCloudRootId = list?.FirstOrDefault(e => e.Code == CloudEntityCodes.CloudRoot)?.Id
            ?? await _api.AddEntityAsync(CloudEntityCodes.CloudRoot, new[] { userId });
        _history.Clear();
        _locations.Clear();
        var lang = _settingsService.LoadSettings().UiLanguage;
        var hubName = ShareSheetStrings.CloudHub(lang);
        await OpenAsync(
            new CloudEntityRef(CloudVirtualNodes.HubId, CloudEntityKind.Folder, hubName, null),
            new[] { new CloudBreadcrumbEntry { Id = CloudVirtualNodes.HubId, Name = hubName } },
            pushHistory: false);
    }

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
            List<CloudItemViewModel> fresh;
            if (folderId == CloudVirtualNodes.HubId)
            {
                fresh = BuildHubListing();
            }
            else if (folderId == CloudVirtualNodes.SharedWithMeId)
            {
                fresh = await LoadSharedWithMeListingAsync();
            }
            else
            {
                var list = await _api.GetEntitiesAsync(new[] { folderId }, includeValues: true);
                if (ListedFolderId != folderId) return;

                var syncedIds = new HashSet<Guid>(SyncMappings.Select(m => m.CloudFolderId));
                fresh = (list ?? new List<GetEntitiesResult>())
                    .Select(e => new CloudItemViewModel
                    {
                        Id = e.Id,
                        Name = CloudApiService.GetDisplayName(e),
                        IsFolder = e.Code == CloudEntityCodes.CloudFolder,
                        IsSynced = e.Code == CloudEntityCodes.CloudFolder && syncedIds.Contains(e.Id),
                        UpdateTime = e.UpdateTime
                    })
                    .OrderByDescending(x => x.IsFolder)
                    .ThenByDescending(x => x.UpdateTime)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            // User may have navigated away while the request was in flight.
            if (ListedFolderId != folderId) return;

            MergeListing(fresh, selectedId, reset: folderChanged);
            _listedFolderId = folderId;
            ClearNetworkBanner();
            if (!quiet)
                StatusText = "Ready";
        }
        catch (Exception ex)
        {
            // Keep last list (MergeListing not called); show offline banner (AT-C6).
            ReportCaughtException(ex, asNetworkFailure: true);
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsSharedWithMeListing));
        OnPropertyChanged(nameof(IsCloudHubListing));
        OnPropertyChanged(nameof(ShowEmptyUpload));
        OnPropertyChanged(nameof(EmptyListTitle));
        OnPropertyChanged(nameof(EmptyListBody));
        OnPropertyChanged(nameof(EmptyListGlyph));
        OnPropertyChanged(nameof(CanMutateListedFolder));
        OnPropertyChanged(nameof(ListedChildCount));
        OnPropertyChanged(nameof(ListedChildCountText));
        OnPropertyChanged(nameof(FolderSizeSummary));
        OnPropertyChanged(nameof(IsInSharedWithMeScope));
        OnPropertyChanged(nameof(ShowSharedBrowseOnlyHint));
        OnPropertyChanged(nameof(SharedBrowseOnlyHintText));
        OnPropertyChanged(nameof(CanMapCurrentFolder));
    }

    private List<CloudItemViewModel> BuildHubListing()
    {
        var lang = _settingsService.LoadSettings().UiLanguage;
        return new List<CloudItemViewModel>
        {
            new()
            {
                Id = _myCloudRootId,
                Name = ShareSheetStrings.MyCloud(lang),
                IsFolder = true,
                IsVirtualEntry = false,
                TypeGlyphOverride = "📁",
                SubtitleOverride = string.Empty,
                UpdateTime = DateTime.UtcNow
            },
            new()
            {
                Id = CloudVirtualNodes.SharedWithMeId,
                Name = ShareSheetStrings.SharedWithMe(lang),
                IsFolder = true,
                IsVirtualEntry = true,
                TypeGlyphOverride = "👥",
                SubtitleOverride = string.Empty,
                UpdateTime = DateTime.UtcNow
            }
        };
    }

    private async Task<List<CloudItemViewModel>> LoadSharedWithMeListingAsync()
    {
        var lang = _settingsService.LoadSettings().UiLanguage;
        var list = await _api.GetSharedWithMeAsync(includeValues: true);
        _shareRootCaptions.Clear();
        _shareRootCanWrite.Clear();

        var creatorIds = list
            .Where(e => e.UserWhoCreatedId is { } uid && uid != Guid.Empty)
            .Select(e => e.UserWhoCreatedId!.Value)
            .Distinct()
            .ToArray();
        var creatorNames = new Dictionary<Guid, string>();
        if (creatorIds.Length > 0)
        {
            var creators = await _api.GetEntitiesByIdsAsync(creatorIds, includeValues: true);
            foreach (var c in creators)
                creatorNames[c.Id] = CloudApiService.GetDisplayName(c);
        }

        var syncedIds = new HashSet<Guid>(SyncMappings.Select(m => m.CloudFolderId));
        var fresh = new List<CloudItemViewModel>();
        foreach (var e in list
                     .OrderByDescending(x => x.Code == CloudEntityCodes.CloudFolder)
                     .ThenByDescending(x => x.UpdateTime)
                     .ThenBy(x => CloudApiService.GetDisplayName(x), StringComparer.OrdinalIgnoreCase))
        {
            var canWrite = e.CanWrite == true;
            string creatorName;
            if (e.UserWhoCreatedId is { } cid && creatorNames.TryGetValue(cid, out var n) && !string.IsNullOrWhiteSpace(n))
                creatorName = n;
            else if (e.UserWhoCreatedId is { } missing)
                creatorName = missing.ToString("N")[..8];
            else
                creatorName = "—";
            var caption = ShareSheetStrings.SharedRowSubtitle(lang, creatorName, canWrite);
            _shareRootCaptions[e.Id] = caption;
            _shareRootCanWrite[e.Id] = canWrite;
            fresh.Add(new CloudItemViewModel
            {
                Id = e.Id,
                Name = CloudApiService.GetDisplayName(e),
                IsFolder = e.Code is CloudEntityCodes.CloudFolder or CloudEntityCodes.CloudRoot,
                IsSynced = e.Code == CloudEntityCodes.CloudFolder && syncedIds.Contains(e.Id),
                IsShareRoot = true,
                CanWriteShare = canWrite,
                SubtitleOverride = caption,
                UpdateTime = e.UpdateTime
            });
        }

        return fresh;
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
            existing.UpdateTime = want.UpdateTime;
            existing.IsVirtualEntry = want.IsVirtualEntry;
            existing.IsShareRoot = want.IsShareRoot;
            existing.CanWriteShare = want.CanWriteShare;
            existing.SubtitleOverride = want.SubtitleOverride;
            existing.TypeGlyphOverride = want.TypeGlyphOverride;
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

    private void ReportCaughtException(Exception ex, bool asNetworkFailure = false)
    {
        if (CloudFolderSynchronizer.IsAuthFailure(ex))
        {
            ClearNetworkBanner();
            _lastError = null;
            OnPropertyChanged(nameof(HasErrorDetails));
            StatusText = "Please log in.";
            _onAuthRequired?.Invoke();
            return;
        }

        if (asNetworkFailure)
        {
            SetNetworkBanner();
            return;
        }

        ClearNetworkBanner();
        _lastError = ex;
        OnPropertyChanged(nameof(HasErrorDetails));
        StatusText = "Something went wrong. See details.";
        CommandManager.InvalidateRequerySuggested();
    }
}
