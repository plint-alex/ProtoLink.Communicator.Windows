using System.Collections.ObjectModel;
using System.Windows.Input;
using ProtoLink.Communicator.Windows.Models.Share;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;

namespace ProtoLink.Communicator.Windows.ViewModels.Share;

/// <summary>Native Cloud Share side sheet (UX §§2–3 / AT-S41).</summary>
public sealed class ShareSheetViewModel : ViewModelBase
{
    public const int MaxLiveGuestLinks = 20;

    private readonly CloudApiService _api;
    private readonly Func<string?> _uiLanguage;
    private readonly Action<string>? _onStatus;
    private readonly Action<string>? _onSendInMessenger;
    private Guid _entityId;
    private string _entityName = "";
    private bool _isFolder;
    private int _childCount;
    private bool _canEdit = true;
    private bool _isOnline = true;
    private bool _isOpen;
    private bool _isLoading;
    private bool _showCreateForm;
    private bool _showCreatedPanel;
    private bool _showCloseWithoutCopy;
    private bool _createdUrlCopied;
    private bool _showInactiveExpanded;
    private string? _createdUrl;
    private string? _newLabel;
    private int _expiryChoice = 30;
    private string? _errorText;
    private bool _busy;

    public ShareSheetViewModel(
        CloudApiService api,
        Func<string?> uiLanguage,
        Action<string>? onStatus = null,
        Action<string>? onSendInMessenger = null)
    {
        _api = api;
        _uiLanguage = uiLanguage;
        _onStatus = onStatus;
        _onSendInMessenger = onSendInMessenger;

        LiveLinks = new ObservableCollection<ShareGuestLinkItemViewModel>();
        InactiveLinks = new ObservableCollection<ShareGuestLinkItemViewModel>();
        People = new ObservableCollection<SharePersonItemViewModel>();

        CloseCommand = new RelayCommand(_ => RequestClose());
        CopyCanonicalLinkCommand = new RelayCommand(
            _ => CopyCanonicalLink?.Invoke(),
            _ => IsOpen && CopyCanonicalLink != null);
        BeginCreateCommand = new RelayCommand(
            _ =>
            {
                ShowCreateForm = true;
                ShowCreatedPanel = false;
                ShowCloseWithoutCopy = false;
                CreatedUrl = null;
                CreatedUrlCopied = false;
                NewLabel = null;
                ExpiryChoice = 30;
            },
            _ => CanMutate && !ShowCreateForm && !ShowCreatedPanel && LiveLinks.Count < MaxLiveGuestLinks);
        CancelCreateCommand = new RelayCommand(_ =>
        {
            ShowCreateForm = false;
            NewLabel = null;
        });
        CreateLinkCommand = new RelayCommand(
            async _ => await CreateLinkAsync(),
            _ => CanMutate && ShowCreateForm && !_busy);
        CopyCreatedUrlCommand = new RelayCommand(
            _ => CopyCreatedUrl(),
            _ => !string.IsNullOrEmpty(CreatedUrl));
        DoneCreatedCommand = new RelayCommand(_ => FinishCreatedPanel());
        CopyAndCloseCommand = new RelayCommand(_ =>
        {
            CopyCreatedUrl();
            FinishCreatedPanel(force: true);
        });
        CloseAnywayCommand = new RelayCommand(_ => FinishCreatedPanel(force: true));
        SendCreatedInMessengerCommand = new RelayCommand(
            _ =>
            {
                if (string.IsNullOrEmpty(CreatedUrl)) return;
                _onSendInMessenger?.Invoke(CreatedUrl);
            },
            _ => !string.IsNullOrEmpty(CreatedUrl) && _onSendInMessenger != null);
        ToggleInactiveCommand = new RelayCommand(_ => ShowInactiveExpanded = !ShowInactiveExpanded);
        RetryLoadCommand = new RelayCommand(async _ => await LoadAsync(), _ => IsOpen && IsOnline);
    }

    public ObservableCollection<ShareGuestLinkItemViewModel> LiveLinks { get; }
    public ObservableCollection<ShareGuestLinkItemViewModel> InactiveLinks { get; }
    public ObservableCollection<SharePersonItemViewModel> People { get; }

    public ICommand CloseCommand { get; }
    public ICommand CopyCanonicalLinkCommand { get; }
    public ICommand BeginCreateCommand { get; }
    public ICommand CancelCreateCommand { get; }
    public ICommand CreateLinkCommand { get; }
    public ICommand CopyCreatedUrlCommand { get; }
    public ICommand DoneCreatedCommand { get; }
    public ICommand CopyAndCloseCommand { get; }
    public ICommand CloseAnywayCommand { get; }
    public ICommand SendCreatedInMessengerCommand { get; }
    public ICommand ToggleInactiveCommand { get; }
    public ICommand RetryLoadCommand { get; }

    public Action? CopyCanonicalLink { get; set; }
    public event Action? Closed;

    public Guid EntityId => _entityId;
    public string EntityName => _entityName;
    public bool IsFolder => _isFolder;

    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (_isOpen == value) return;
            _isOpen = value;
            OnPropertyChanged();
            NotifyChrome();
            CommandManager.InvalidateRequerySuggested();
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

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (_isOnline == value) return;
            _isOnline = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanMutate));
            OnPropertyChanged(nameof(ShowOfflineBanner));
            OnPropertyChanged(nameof(MutationTooltip));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool CanEdit
    {
        get => _canEdit;
        set
        {
            if (_canEdit == value) return;
            _canEdit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanMutate));
            OnPropertyChanged(nameof(ShowViewerFooter));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool CanMutate => IsOnline && CanEdit && !ShowCreatedPanel;
    public bool ShowOfflineBanner => IsOpen && !IsOnline;
    public bool ShowViewerFooter => IsOpen && !CanEdit;

    public bool ShowCreateForm
    {
        get => _showCreateForm;
        private set
        {
            if (_showCreateForm == value) return;
            _showCreateForm = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool ShowCreatedPanel
    {
        get => _showCreatedPanel;
        private set
        {
            if (_showCreatedPanel == value) return;
            _showCreatedPanel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanMutate));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool ShowCloseWithoutCopy
    {
        get => _showCloseWithoutCopy;
        private set
        {
            if (_showCloseWithoutCopy == value) return;
            _showCloseWithoutCopy = value;
            OnPropertyChanged();
        }
    }

    public bool CreatedUrlCopied
    {
        get => _createdUrlCopied;
        private set
        {
            if (_createdUrlCopied == value) return;
            _createdUrlCopied = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CopyCreatedButtonLabel));
        }
    }

    public string? CreatedUrl
    {
        get => _createdUrl;
        private set
        {
            if (_createdUrl == value) return;
            _createdUrl = value;
            OnPropertyChanged();
        }
    }

    public string? NewLabel
    {
        get => _newLabel;
        set
        {
            if (_newLabel == value) return;
            _newLabel = value;
            OnPropertyChanged();
        }
    }

    public int ExpiryChoice
    {
        get => _expiryChoice;
        set
        {
            if (_expiryChoice == value) return;
            _expiryChoice = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowNeverHint));
            OnPropertyChanged(nameof(IsExpiry1));
            OnPropertyChanged(nameof(IsExpiry7));
            OnPropertyChanged(nameof(IsExpiry30));
            OnPropertyChanged(nameof(IsExpiryNever));
        }
    }

    public bool IsExpiry1 { get => ExpiryChoice == 1; set { if (value) ExpiryChoice = 1; } }
    public bool IsExpiry7 { get => ExpiryChoice == 7; set { if (value) ExpiryChoice = 7; } }
    public bool IsExpiry30 { get => ExpiryChoice == 30; set { if (value) ExpiryChoice = 30; } }
    public bool IsExpiryNever { get => ExpiryChoice == 0; set { if (value) ExpiryChoice = 0; } }
    public bool ShowNeverHint => ExpiryChoice == 0;

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (_errorText == value) return;
            _errorText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public bool ShowInactiveExpanded
    {
        get => _showInactiveExpanded;
        private set
        {
            if (_showInactiveExpanded == value) return;
            _showInactiveExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowInactiveList));
        }
    }

    public bool ShowInactiveList => ShowInactiveExpanded && InactiveLinks.Count > 0;
    public bool HasInactiveLinks => InactiveLinks.Count > 0;
    public bool HasLiveLinks => LiveLinks.Count > 0;
    public bool ShowEmptyGuestLinks => !IsLoading && !HasLiveLinks && !ShowCreateForm && !ShowCreatedPanel;
    public bool ShowLinkLimit => LiveLinks.Count >= MaxLiveGuestLinks;
    public bool ShowSubtreeLine => _isFolder;
    public bool ShowCreateButton => CanMutate && !ShowCreateForm && !ShowCreatedPanel && !ShowLinkLimit;

    public string? Lang => _uiLanguage();
    public string TitleText => ShareSheetStrings.SheetTitle(Lang, _entityName);
    public string SubtitleText
    {
        get
        {
            if (_isFolder)
            {
                var items = _childCount == 1
                    ? (ShareSheetStrings.IsRussian(Lang) ? "1 элемент" : "1 item")
                    : (ShareSheetStrings.IsRussian(Lang) ? $"{_childCount} элементов" : $"{_childCount} items");
                return ShareSheetStrings.IsRussian(Lang) ? $"Папка · {items}" : $"Folder · {items}";
            }

            return ShareSheetStrings.IsRussian(Lang) ? "Файл" : "File";
        }
    }

    public string LinkSectionLabel => ShareSheetStrings.LinkSection(Lang);
    public string LinkHelperText => ShareSheetStrings.LinkHelper(Lang);
    public string CopyLinkLabel => ShareSheetStrings.CopyLink(Lang);
    public string GuestLinksSectionLabel => ShareSheetStrings.GuestLinksSection(Lang);
    public string GuestLinksHelperText => ShareSheetStrings.GuestLinksHelper(Lang);
    public string SubtreeLineText => ShareSheetStrings.SubtreeLine(Lang);
    public string CreateGuestLinkLabel => ShareSheetStrings.CreateGuestLink(Lang);
    public string NoGuestLinksText => ShareSheetStrings.NoGuestLinksYet(Lang);
    public string PeopleSectionLabel => ShareSheetStrings.PeopleSection(Lang);
    public string FooterLagText => ShareSheetStrings.FooterLag(Lang);
    public string OfflineBannerText => ShareSheetStrings.OfflineBanner(Lang);
    public string MutationTooltip =>
        IsOnline ? "" : ShareSheetStrings.OfflineMutationTooltip(Lang);
    public string ViewerFooterText => ShareSheetStrings.ViewerOnlyFooter(Lang);
    public string LinkLimitText => ShareSheetStrings.LinkLimit(Lang);
    public string CancelLabel => ShareSheetStrings.Cancel(Lang);
    public string CreateLinkLabel => ShareSheetStrings.CreateLink(Lang);
    public string NameOptionalLabel => ShareSheetStrings.NameOptional(Lang);
    public string NamePlaceholder => ShareSheetStrings.NamePlaceholder(Lang);
    public string ExpiresLabel => ShareSheetStrings.Expires(Lang);
    public string Expiry1Label => ShareSheetStrings.Expiry1Day(Lang);
    public string Expiry7Label => ShareSheetStrings.Expiry7Days(Lang);
    public string Expiry30Label => ShareSheetStrings.Expiry30Days(Lang);
    public string ExpiryNeverLabel => ShareSheetStrings.ExpiryNever(Lang);
    public string NeverExpiresHint => ShareSheetStrings.NeverExpiresHint(Lang);
    public string GuestLinkCreatedLabel => ShareSheetStrings.GuestLinkCreated(Lang);
    public string CopyOnceHint => ShareSheetStrings.CopyOnceHint(Lang);
    public string LostLinkHint => ShareSheetStrings.LostLinkHint(Lang);
    public string DoneLabel => ShareSheetStrings.Done(Lang);
    public string CopyCreatedButtonLabel =>
        CreatedUrlCopied ? "✓ " + ShareSheetStrings.Copied(Lang) : ShareSheetStrings.CopyLink(Lang);
    public string CloseWithoutCopyText => ShareSheetStrings.CloseWithoutCopy(Lang);
    public string CopyAndCloseLabel => ShareSheetStrings.CopyAndClose(Lang);
    public string CloseAnywayLabel => ShareSheetStrings.CloseAnyway(Lang);
    public string SendInMessengerLabel => ShareSheetStrings.SendInMessenger(Lang);
    public string InactiveLinksLabel => ShareSheetStrings.InactiveLinks(Lang, InactiveLinks.Count);

    public async Task OpenAsync(
        Guid entityId,
        string entityName,
        bool isFolder,
        int childCount,
        bool canEdit,
        bool isOnline)
    {
        _entityId = entityId;
        _entityName = entityName ?? "";
        _isFolder = isFolder;
        _childCount = childCount;
        CanEdit = canEdit;
        IsOnline = isOnline;
        ShowCreateForm = false;
        ShowCreatedPanel = false;
        ShowCloseWithoutCopy = false;
        CreatedUrl = null;
        CreatedUrlCopied = false;
        ErrorText = null;
        ShowInactiveExpanded = false;
        IsOpen = true;
        NotifyChrome();
        await LoadAsync();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        ShowCreateForm = false;
        ShowCreatedPanel = false;
        ShowCloseWithoutCopy = false;
        CreatedUrl = null;
        LiveLinks.Clear();
        InactiveLinks.Clear();
        People.Clear();
        Closed?.Invoke();
    }

    private void RequestClose()
    {
        if (ShowCreatedPanel && !CreatedUrlCopied && !ShowCloseWithoutCopy)
        {
            ShowCloseWithoutCopy = true;
            return;
        }

        Close();
    }

    private void FinishCreatedPanel(bool force = false)
    {
        if (!force && !CreatedUrlCopied)
        {
            ShowCloseWithoutCopy = true;
            return;
        }

        ShowCreatedPanel = false;
        ShowCloseWithoutCopy = false;
        CreatedUrl = null;
        CreatedUrlCopied = false;
        _ = LoadAsync();
    }

    private void CopyCreatedUrl()
    {
        if (string.IsNullOrEmpty(CreatedUrl)) return;
        try
        {
            System.Windows.Clipboard.SetText(CreatedUrl);
            CreatedUrlCopied = true;
            ShowCloseWithoutCopy = false;
            _onStatus?.Invoke(ShareSheetStrings.Copied(Lang));
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
    }

    public async Task LoadAsync()
    {
        if (!IsOpen) return;
        if (!IsOnline)
        {
            ErrorText = null;
            return;
        }

        IsLoading = true;
        ErrorText = null;
        try
        {
            var linksTask = _api.ListShareLinksAsync(_entityId);
            var grantsTask = _api.ListShareGrantsAsync(_entityId);
            await Task.WhenAll(linksTask, grantsTask);
            ApplyLinks(await linksTask);
            ApplyGrants(await grantsTask);
        }
        catch (Exception ex)
        {
            ErrorText = ShareSheetStrings.LoadFailed(Lang);
            _onStatus?.Invoke(ErrorText + " " + ex.Message);
        }
        finally
        {
            IsLoading = false;
            NotifyLists();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task CreateLinkAsync()
    {
        if (_busy || !CanMutate) return;
        _busy = true;
        ErrorText = null;
        try
        {
            var now = DateTime.UtcNow;
            var never = ExpiryChoice == 0;
            DateTime? expires = never
                ? null
                : now.AddDays(ExpiryChoice);

            var result = await _api.CreateShareLinkAsync(new CreateShareLinkContract
            {
                EntityId = _entityId,
                Label = string.IsNullOrWhiteSpace(NewLabel) ? null : NewLabel.Trim(),
                ExpiresAt = expires,
                NeverExpires = never,
                IncludeSubtree = _isFolder,
                CanWrite = false
            });

            ShowCreateForm = false;
            NewLabel = null;
            CreatedUrl = result.Url;
            CreatedUrlCopied = false;
            ShowCloseWithoutCopy = false;
            ShowCreatedPanel = true;
        }
        catch (Exception)
        {
            ErrorText = ShareSheetStrings.CreateFailed(Lang);
            _onStatus?.Invoke(ErrorText);
        }
        finally
        {
            _busy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task TurnOffAsync(ShareGuestLinkItemViewModel item)
    {
        if (!CanMutate) return;
        try
        {
            await _api.RevokeShareLinkAsync(item.Id);
            _onStatus?.Invoke(ShareSheetStrings.GuestLinkTurnedOff(Lang));
            await LoadAsync();
        }
        catch (Exception)
        {
            ErrorText = ShareSheetStrings.RevokeFailed(Lang);
            _onStatus?.Invoke(ErrorText);
        }
    }

    private async Task RemovePersonAsync(SharePersonItemViewModel item)
    {
        if (!CanMutate || item.IsCreator) return;
        try
        {
            await _api.RemovePermissionAsync(_entityId, item.Grant.PermissionForId);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            _onStatus?.Invoke(ErrorText);
        }
    }

    private void ApplyLinks(IReadOnlyList<ShareLinkListItemResult> links)
    {
        LiveLinks.Clear();
        InactiveLinks.Clear();
        foreach (var link in links.OrderByDescending(l => l.CreatedAt))
        {
            var vm = new ShareGuestLinkItemViewModel(
                link,
                Lang,
                TurnOffAsync,
                () => CanMutate);
            if (link.IsLive)
                LiveLinks.Add(vm);
            else
                InactiveLinks.Add(vm);
        }
    }

    private void ApplyGrants(IReadOnlyList<ShareGrantResult> grants)
    {
        People.Clear();
        foreach (var g in grants)
        {
            if (string.Equals(g.PrincipalKind, "guest_link", StringComparison.OrdinalIgnoreCase))
                continue;

            // Creator heuristic: first user with CanWrite and empty ShareLinkId — sheet marks via display
            var isCreator = false;
            People.Add(new SharePersonItemViewModel(
                g,
                Lang,
                isCreator,
                RemovePersonAsync,
                () => CanMutate));
        }
    }

    private void NotifyChrome()
    {
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(LinkSectionLabel));
        OnPropertyChanged(nameof(LinkHelperText));
        OnPropertyChanged(nameof(CopyLinkLabel));
        OnPropertyChanged(nameof(GuestLinksSectionLabel));
        OnPropertyChanged(nameof(GuestLinksHelperText));
        OnPropertyChanged(nameof(SubtreeLineText));
        OnPropertyChanged(nameof(CreateGuestLinkLabel));
        OnPropertyChanged(nameof(NoGuestLinksText));
        OnPropertyChanged(nameof(PeopleSectionLabel));
        OnPropertyChanged(nameof(FooterLagText));
        OnPropertyChanged(nameof(OfflineBannerText));
        OnPropertyChanged(nameof(MutationTooltip));
        OnPropertyChanged(nameof(ViewerFooterText));
        OnPropertyChanged(nameof(LinkLimitText));
        OnPropertyChanged(nameof(CancelLabel));
        OnPropertyChanged(nameof(CreateLinkLabel));
        OnPropertyChanged(nameof(NameOptionalLabel));
        OnPropertyChanged(nameof(NamePlaceholder));
        OnPropertyChanged(nameof(ExpiresLabel));
        OnPropertyChanged(nameof(Expiry1Label));
        OnPropertyChanged(nameof(Expiry7Label));
        OnPropertyChanged(nameof(Expiry30Label));
        OnPropertyChanged(nameof(ExpiryNeverLabel));
        OnPropertyChanged(nameof(NeverExpiresHint));
        OnPropertyChanged(nameof(GuestLinkCreatedLabel));
        OnPropertyChanged(nameof(CopyOnceHint));
        OnPropertyChanged(nameof(LostLinkHint));
        OnPropertyChanged(nameof(DoneLabel));
        OnPropertyChanged(nameof(CopyCreatedButtonLabel));
        OnPropertyChanged(nameof(CloseWithoutCopyText));
        OnPropertyChanged(nameof(CopyAndCloseLabel));
        OnPropertyChanged(nameof(CloseAnywayLabel));
        OnPropertyChanged(nameof(SendInMessengerLabel));
        OnPropertyChanged(nameof(ShowSubtreeLine));
        OnPropertyChanged(nameof(ShowCreateButton));
        OnPropertyChanged(nameof(ShowEmptyGuestLinks));
        OnPropertyChanged(nameof(ShowLinkLimit));
        OnPropertyChanged(nameof(ShowOfflineBanner));
        OnPropertyChanged(nameof(ShowViewerFooter));
        OnPropertyChanged(nameof(CanMutate));
        NotifyLists();
    }

    private void NotifyLists()
    {
        OnPropertyChanged(nameof(HasLiveLinks));
        OnPropertyChanged(nameof(HasInactiveLinks));
        OnPropertyChanged(nameof(ShowInactiveList));
        OnPropertyChanged(nameof(InactiveLinksLabel));
        OnPropertyChanged(nameof(ShowEmptyGuestLinks));
        OnPropertyChanged(nameof(ShowLinkLimit));
        OnPropertyChanged(nameof(ShowCreateButton));
    }
}
