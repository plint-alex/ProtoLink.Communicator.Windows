using System.Collections.ObjectModel;
using System.Windows.Input;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.ViewModels.Share;

public sealed partial class ShareSheetViewModel
{
    private readonly Func<Guid?> _currentUserId;
    private bool _showAddPeople;
    private bool _showAddedBanner;
    private string? _contactSearch;
    private string? _pasteId;
    private string? _pastePreviewName;
    private string? _pastePreviewError;
    private bool _addCanWrite;
    private string? _addedBannerText;
    private Guid? _pasteResolvedUserId;
    private CancellationTokenSource? _pasteResolveCts;
    private List<(Guid UserId, string Name)> _allContacts = new();

    public ObservableCollection<ShareContactPickerItemViewModel> ContactPickerItems { get; } = new();

    public ICommand BeginAddPeopleCommand { get; private set; } = null!;
    public ICommand CancelAddPeopleCommand { get; private set; } = null!;
    public ICommand AddPeopleCommand { get; private set; } = null!;
    public ICommand ResolvePasteCommand { get; private set; } = null!;
    public ICommand DismissAddedBannerCommand { get; private set; } = null!;
    public ICommand CopyCanonicalFromAddedCommand { get; private set; } = null!;
    public ICommand SendCanonicalInMessengerCommand { get; private set; } = null!;

    public bool ShowAddPeople
    {
        get => _showAddPeople;
        private set
        {
            if (_showAddPeople == value) return;
            _showAddPeople = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowMainShareBody));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Hide guest-link / people chrome while the Add people panel is open (created panel stays in this tree).</summary>
    public bool ShowMainShareBody => !ShowAddPeople;

    public bool ShowAddedBanner
    {
        get => _showAddedBanner;
        private set
        {
            if (_showAddedBanner == value) return;
            _showAddedBanner = value;
            OnPropertyChanged();
        }
    }

    public string? ContactSearch
    {
        get => _contactSearch;
        set
        {
            if (_contactSearch == value) return;
            _contactSearch = value;
            OnPropertyChanged();
            RebuildContactPicker();
        }
    }

    public string? PasteId
    {
        get => _pasteId;
        set
        {
            if (_pasteId == value) return;
            _pasteId = value;
            OnPropertyChanged();
            PastePreviewName = null;
            PastePreviewError = null;
            _pasteResolvedUserId = null;
            CommandManager.InvalidateRequerySuggested();
            ScheduleAutoResolvePaste();
        }
    }

    public string? PastePreviewName
    {
        get => _pastePreviewName;
        private set
        {
            if (_pastePreviewName == value) return;
            _pastePreviewName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPastePreview));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? PastePreviewError
    {
        get => _pastePreviewError;
        private set
        {
            if (_pastePreviewError == value) return;
            _pastePreviewError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPastePreviewError));
        }
    }

    public bool HasPastePreview => !string.IsNullOrEmpty(PastePreviewName);
    public bool HasPastePreviewError => !string.IsNullOrEmpty(PastePreviewError);

    public bool AddCanWrite
    {
        get => _addCanWrite;
        set
        {
            if (_addCanWrite == value) return;
            _addCanWrite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AddCanView));
            OnPropertyChanged(nameof(ShowCanEditHelper));
        }
    }

    public bool AddCanView
    {
        get => !_addCanWrite;
        set { if (value) AddCanWrite = false; }
    }

    public bool ShowCanEditHelper => AddCanWrite;

    public string? AddedBannerText
    {
        get => _addedBannerText;
        private set
        {
            if (_addedBannerText == value) return;
            _addedBannerText = value;
            OnPropertyChanged();
        }
    }

    public string AddPeopleButtonLabel => ShareSheetStrings.AddPeopleButton(Lang);
    public string AddPeopleTitleLabel => ShareSheetStrings.AddPeopleTitle(Lang);
    public string SearchContactsLabel => ShareSheetStrings.SearchContacts(Lang);
    public string PasteIdHintLabel => ShareSheetStrings.PasteIdHint(Lang);
    public string PasteIdPlaceholderLabel => ShareSheetStrings.PasteIdPlaceholder(Lang);
    public string CanEditHelperLabel => ShareSheetStrings.CanEditHelper(Lang);
    public string AddLabel => ShareSheetStrings.Add(Lang);
    public string AccessLabel => ShareSheetStrings.Access(Lang);
    public string CanViewLabel => ShareSheetStrings.CanView(Lang);
    public string CanEditLabel => ShareSheetStrings.CanEdit(Lang);
    public string NoContactsYetLabel => ShareSheetStrings.NoContactsYet(Lang);
    public bool ShowNoContacts => ShowAddPeople && !IsLoading && ContactPickerItems.Count == 0
                                  && string.IsNullOrWhiteSpace(ContactSearch);

    private void InitAddPeopleCommands()
    {
        BeginAddPeopleCommand = new RelayCommand(
            async _ => await BeginAddPeopleAsync(),
            _ => CanMutate && !ShowAddPeople && !ShowCreateForm);
        CancelAddPeopleCommand = new RelayCommand(_ => ResetAddPeoplePanel());
        AddPeopleCommand = new RelayCommand(
            async _ => await AddPeopleAsync(),
            _ => CanMutate && ShowAddPeople && !_busy && HasAddTargets());
        ResolvePasteCommand = new RelayCommand(
            async _ => await ResolvePasteAsync(),
            _ => CanMutate && ShowAddPeople && !_busy && ShareAddPeople.ParseUserIdInput(PasteId).HasValue);
        DismissAddedBannerCommand = new RelayCommand(_ =>
        {
            ShowAddedBanner = false;
            AddedBannerText = null;
        });
        CopyCanonicalFromAddedCommand = new RelayCommand(
            _ => CopyCanonicalLink?.Invoke(),
            _ => CopyCanonicalLink != null);
        SendCanonicalInMessengerCommand = new RelayCommand(
            _ =>
            {
                var url = GetCanonicalLink?.Invoke();
                if (string.IsNullOrEmpty(url)) return;
                _onSendInMessenger?.Invoke(url);
            },
            _ => _onSendInMessenger != null && GetCanonicalLink != null);
    }

    private bool HasAddTargets()
    {
        if (ContactPickerItems.Any(c => c.IsSelected)) return true;
        return _pasteResolvedUserId.HasValue && HasPastePreview;
    }

    private async Task BeginAddPeopleAsync()
    {
        ShowAddedBanner = false;
        ShowAddPeople = true;
        ContactSearch = null;
        PasteId = null;
        AddCanWrite = false;
        await LoadContactsForPickerAsync();
        NotifyAddPeopleChrome();
    }

    private void ResetAddPeoplePanel()
    {
        CancelPendingPasteResolve();
        ShowAddPeople = false;
        ContactSearch = null;
        // Clear paste fields without ScheduleAutoResolvePaste (setter would re-schedule).
        if (_pasteId != null)
        {
            _pasteId = null;
            OnPropertyChanged(nameof(PasteId));
        }
        PastePreviewName = null;
        PastePreviewError = null;
        _pasteResolvedUserId = null;
        AddCanWrite = false;
        ContactPickerItems.Clear();
        _allContacts = new();
        CommandManager.InvalidateRequerySuggested();
    }

    private void CancelPendingPasteResolve()
    {
        try
        {
            _pasteResolveCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }

        _pasteResolveCts?.Dispose();
        _pasteResolveCts = null;
    }

    /// <summary>
    /// share-sheet-ux §4: on a valid GUID / /{id} URL, resolve name without requiring … click.
    /// Invalid / email paste: no resolve (Add stays disabled; cycle-1 has no email lookup).
    /// </summary>
    private void ScheduleAutoResolvePaste()
    {
        CancelPendingPasteResolve();
        if (!ShowAddPeople || !CanMutate || _busy) return;
        if (!ShareAddPeople.ParseUserIdInput(_pasteId).HasValue) return;

        var cts = new CancellationTokenSource();
        _pasteResolveCts = cts;
        var token = cts.Token;
        _ = DebouncedResolvePasteAsync(token);
    }

    private async Task DebouncedResolvePasteAsync(CancellationToken token)
    {
        try
        {
            if (_pasteResolveDebounce > TimeSpan.Zero)
                await Task.Delay(_pasteResolveDebounce, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;
            await ResolvePasteAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer paste or panel reset
        }
    }

    private async Task LoadContactsForPickerAsync()
    {
        ContactPickerItems.Clear();
        _allContacts = new();
        var me = _currentUserId();
        if (me is not { } userId || !IsOnline) return;

        try
        {
            var rows = await _api.ListMessengerContactUsersAsync(userId);
            var already = People
                .Where(p => string.Equals(p.Grant.PrincipalKind, "user", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Grant.PermissionForId)
                .ToHashSet();
            _allContacts = rows
                .Where(r => r.UserId != userId && !already.Contains(r.UserId))
                .Select(r => (r.UserId, r.ProvisionalName))
                .ToList();

            // Enrich names via GetEntity (same resolve as paste) — best-effort, keep provisional on failure.
            var enrich = _allContacts.ToList();
            for (var i = 0; i < enrich.Count; i++)
            {
                var row = enrich[i];
                if (!ShareAddPeople.IsGuidText(row.Name)) continue;
                try
                {
                    var card = await _api.GetEntityCardAsync(row.UserId);
                    var name = ShareAddPeople.PickResolvedDisplayName(card?.Name, card?.Code);
                    if (name != null)
                        enrich[i] = (row.UserId, name);
                }
                catch
                {
                    // keep provisional
                }
            }
            _allContacts = enrich;
            RebuildContactPicker();
        }
        catch (Exception ex)
        {
            // Soft-fail: keep successful List/ListGrants ErrorText (or none). Empty picker + soft status only.
            _allContacts = new();
            ContactPickerItems.Clear();
            OnPropertyChanged(nameof(ShowNoContacts));
            _onStatus?.Invoke(ShareSheetStrings.ContactsLoadSoft(Lang) + " " + ex.Message);
        }
    }

    private void RebuildContactPicker()
    {
        var filtered = ShareAddPeople.FilterContacts(
            _allContacts,
            c => c.Name,
            ContactSearch);
        var selected = ContactPickerItems.Where(c => c.IsSelected).Select(c => c.UserId).ToHashSet();
        ContactPickerItems.Clear();
        foreach (var row in filtered)
        {
            var item = new ShareContactPickerItemViewModel(row.UserId, row.Name, _ =>
            {
                CommandManager.InvalidateRequerySuggested();
            })
            {
                IsSelected = selected.Contains(row.UserId)
            };
            ContactPickerItems.Add(item);
        }
        OnPropertyChanged(nameof(ShowNoContacts));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task ResolvePasteAsync()
    {
        PastePreviewName = null;
        PastePreviewError = null;
        _pasteResolvedUserId = null;
        var raw = PasteId;
        var id = ShareAddPeople.ParseUserIdInput(raw);
        if (id is not { } userId)
        {
            PastePreviewError = ShareSheetStrings.NoUserWithId(Lang);
            return;
        }

        try
        {
            var card = await _api.GetEntityCardAsync(userId);
            // Stale if the user changed paste while GetEntity was in flight.
            if (!string.Equals(PasteId, raw, StringComparison.Ordinal)) return;
            var name = ShareAddPeople.PickResolvedDisplayName(card?.Name, card?.Code);
            if (name == null)
            {
                PastePreviewError = ShareSheetStrings.NoUserWithId(Lang);
                return;
            }
            _pasteResolvedUserId = userId;
            PastePreviewName = name;
        }
        catch
        {
            if (!string.Equals(PasteId, raw, StringComparison.Ordinal)) return;
            PastePreviewError = ShareSheetStrings.NoUserWithId(Lang);
        }
    }

    private async Task AddPeopleAsync()
    {
        if (_busy || !CanMutate || !HasAddTargets()) return;
        _busy = true;
        ErrorText = null;
        try
        {
            var targets = new Dictionary<Guid, string>();
            foreach (var c in ContactPickerItems.Where(x => x.IsSelected))
                targets[c.UserId] = c.Name;
            if (_pasteResolvedUserId is { } pasteId && HasPastePreview && PastePreviewName != null)
                targets[pasteId] = PastePreviewName;

            foreach (var (userId, _) in targets)
                await _api.AddPermissionAsync(_entityId, userId, AddCanWrite);

            var firstName = targets.Values.First();
            var toast = ShareSheetStrings.AddedToast(Lang, firstName, AddCanWrite);
            if (targets.Count > 1)
            {
                toast = ShareSheetStrings.IsRussian(Lang)
                    ? $"{targets.Count} человек добавлено. Отправьте ссылку, чтобы найти."
                    : $"{targets.Count} people can now access this. Send them the link so they can find it.";
            }

            AddedBannerText = toast;
            ShowAddedBanner = true;
            _onStatus?.Invoke(toast);
            ResetAddPeoplePanel();
            await LoadAsync();
        }
        catch (Exception)
        {
            ErrorText = ShareSheetStrings.AddFailed(Lang);
            _onStatus?.Invoke(ErrorText);
        }
        finally
        {
            _busy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void NotifyAddPeopleChrome()
    {
        OnPropertyChanged(nameof(AddPeopleButtonLabel));
        OnPropertyChanged(nameof(AddPeopleTitleLabel));
        OnPropertyChanged(nameof(SearchContactsLabel));
        OnPropertyChanged(nameof(PasteIdHintLabel));
        OnPropertyChanged(nameof(PasteIdPlaceholderLabel));
        OnPropertyChanged(nameof(CanEditHelperLabel));
        OnPropertyChanged(nameof(AddLabel));
        OnPropertyChanged(nameof(AccessLabel));
        OnPropertyChanged(nameof(CanViewLabel));
        OnPropertyChanged(nameof(CanEditLabel));
        OnPropertyChanged(nameof(NoContactsYetLabel));
        OnPropertyChanged(nameof(ShowNoContacts));
        OnPropertyChanged(nameof(ShowMainShareBody));
        OnPropertyChanged(nameof(ShowCanEditHelper));
        OnPropertyChanged(nameof(SendInMessengerLabel));
        OnPropertyChanged(nameof(CopyLinkLabel));
        OnPropertyChanged(nameof(CancelLabel));
    }
}
