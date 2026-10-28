using System.Collections.ObjectModel;
using System.Windows.Input;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.ViewModels;

/// <summary>M3 — navigable Cloud entity picker (not an OS file dialog).</summary>
public sealed class CloudPickerViewModel : ViewModelBase
{
    private readonly CloudApiService _api;
    private readonly Guid _myUserId;
    private readonly Guid _recipientUserId;
    private readonly Stack<(Guid Id, string Name)> _folderStack = new();
    private string _headerSubtitle = "Cloud";
    private string? _statusText;
    private bool _isLoading;
    private string _filterText = string.Empty;
    private List<CloudPickerItemViewModel> _allItems = new();

    public CloudPickerViewModel(CloudApiService api, Guid myUserId, Guid recipientUserId)
    {
        _api = api;
        _myUserId = myUserId;
        _recipientUserId = recipientUserId;
        Items = new ObservableCollection<CloudPickerItemViewModel>();
        ActivateItemCommand = new RelayCommand(
            p => _ = ActivateAsync(p as CloudPickerItemViewModel),
            p => p is CloudPickerItemViewModel item && (item.IsFolder || item.CanSelect));
        GoBackCommand = new RelayCommand(_ => _ = GoBackAsync(), _ => _folderStack.Count > 1);
        CloseCommand = new RelayCommand(_ => RequestClose?.Invoke(null));
    }

    public ObservableCollection<CloudPickerItemViewModel> Items { get; }

    public string Title => "Choose from Cloud";

    public string HeaderSubtitle
    {
        get => _headerSubtitle;
        private set
        {
            if (_headerSubtitle == value) return;
            _headerSubtitle = value;
            OnPropertyChanged();
        }
    }

    public string? StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (_filterText == value) return;
            _filterText = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public ICommand ActivateItemCommand { get; }
    public ICommand GoBackCommand { get; }
    public ICommand CloseCommand { get; }

    /// <summary>Invoked with the selected CloudFile entity, or null when cancelled.</summary>
    public Action<CloudPickerSelection?>? RequestClose { get; set; }

    public async Task OpenAtRootAsync()
    {
        _folderStack.Clear();
        var rootId = await ResolveCloudRootAsync().ConfigureAwait(true);
        _folderStack.Push((rootId, "Cloud"));
        HeaderSubtitle = "Cloud";
        await LoadCurrentFolderAsync().ConfigureAwait(true);
    }

    private async Task ActivateAsync(CloudPickerItemViewModel? item)
    {
        if (item == null) return;
        if (item.IsFolder)
        {
            _folderStack.Push((item.Id, item.Name));
            HeaderSubtitle = string.Join(" › ", _folderStack.Reverse().Select(x => x.Name));
            CommandManager.InvalidateRequerySuggested();
            await LoadCurrentFolderAsync().ConfigureAwait(true);
            return;
        }

        if (!item.CanSelect) return;
        RequestClose?.Invoke(new CloudPickerSelection(item.Id, item.Name, item.MimeType));
    }

    private async Task GoBackAsync()
    {
        if (_folderStack.Count <= 1) return;
        _folderStack.Pop();
        HeaderSubtitle = string.Join(" › ", _folderStack.Reverse().Select(x => x.Name));
        CommandManager.InvalidateRequerySuggested();
        await LoadCurrentFolderAsync().ConfigureAwait(true);
    }

    private async Task LoadCurrentFolderAsync()
    {
        if (_folderStack.Count == 0) return;
        var folderId = _folderStack.Peek().Id;
        IsLoading = true;
        StatusText = null;
        try
        {
            var list = await _api.GetEntitiesAsync(new[] { folderId }, includeValues: true)
                .ConfigureAwait(true);
            var rows = (list ?? new List<GetEntitiesResult>())
                .Select(e => new CloudPickerItemViewModel
                {
                    Id = e.Id,
                    Name = CloudApiService.GetDisplayName(e),
                    IsFolder = e.Code == CloudEntityCodes.CloudFolder
                               || e.Code == CloudEntityCodes.CloudRoot,
                    MimeType = CloudApiService.GetMimeType(e),
                    UpdateTime = e.UpdateTime,
                    Subtitle = e.Code == CloudEntityCodes.CloudFolder || e.Code == CloudEntityCodes.CloudRoot
                        ? "Folder"
                        : TypeSubtitle(e),
                    CanSelect = true
                })
                .OrderByDescending(x => x.IsFolder)
                .ThenByDescending(x => x.UpdateTime)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Shareability for selectable (non-folder) rows — parallel GetPermissions.
            var files = rows.Where(r => !r.IsFolder).ToList();
            await Task.WhenAll(files.Select(async row =>
            {
                try
                {
                    var perms = await _api.GetPermissionsAsync(row.Id).ConfigureAwait(false);
                    var senderWrite = perms.Any(p =>
                        p.PermissionForId == _myUserId && p.CanWrite);
                    var recipientRead = _recipientUserId == _myUserId
                        || perms.Any(p => p.PermissionForId == _recipientUserId);
                    var ok = MessengerCloudShareability.CanSelect(senderWrite, recipientRead);
                    row.CanSelect = ok;
                    if (!ok)
                        row.Subtitle = MessengerCloudShareability.DisabledShareSubtitleEn;
                }
                catch
                {
                    // Fail closed: cannot verify grant → disable (honest refusal).
                    row.CanSelect = false;
                    row.Subtitle = MessengerCloudShareability.DisabledShareSubtitleEn;
                }
            })).ConfigureAwait(true);

            _allItems = rows;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusText = "Waiting for network…";
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        Items.Clear();
        var q = FilterText.Trim();
        IEnumerable<CloudPickerItemViewModel> src = _allItems;
        if (q.Length > 0)
            src = src.Where(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        foreach (var i in src)
            Items.Add(i);
    }

    private async Task<Guid> ResolveCloudRootAsync()
    {
        var underUser = await _api.GetEntitiesAsync(new[] { _myUserId }, includeValues: false)
            .ConfigureAwait(true);
        var candidates = underUser
            .Where(e => e.Code == CloudEntityCodes.CloudRoot)
            .Select(e => new CloudRootSelection.Candidate(e.Id, e.CreationTime))
            .ToList();
        var picked = CloudRootSelection.PickCanonical(candidates);
        if (picked is { } id && id != Guid.Empty)
            return id;
        return await _api.AddEntityAsync(CloudEntityCodes.CloudRoot, new[] { _myUserId })
            .ConfigureAwait(true);
    }

    private static string TypeSubtitle(GetEntitiesResult e)
    {
        var mime = CloudApiService.GetMimeType(e);
        if (MessengerAttachmentRender.LooksLikeImageMime(mime))
            return "File · image";
        return "File";
    }
}

public readonly record struct CloudPickerSelection(Guid EntityId, string Name, string? MimeType);
