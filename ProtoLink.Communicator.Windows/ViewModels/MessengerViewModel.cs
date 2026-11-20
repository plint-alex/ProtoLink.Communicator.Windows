using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Messenger;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class MessengerViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly HttpClient _httpClient;
    private readonly ISettingsService? _settingsService;
    private Contact? _selectedContact;
    private string _messageText = string.Empty;
    private bool _isCompactLayout;
    private bool _isChatLoading;
    private bool _isContactsLoading;
    private int _contactSelectGeneration;
    private int _contactsLoadGeneration;
    private Guid? _userContactsId;
    private Guid? _mySentContainerId;
    private Guid? _myReceivedContainerId;
    private readonly Dictionary<Guid, Guid?> _partnerSentContainerIds = new();
    private readonly Dictionary<Guid, Guid?> _partnerReceivedContainerIds = new();
    private readonly Dictionary<Guid, List<MessageViewModel>> _cachedMessages = new();
    private readonly HashSet<Guid> _loadedContacts = new();
    private readonly Dictionary<Guid, string> _userLoginCache = new();
    private readonly MessengerDiskCache _diskCache;
    private DispatcherTimer? _chatPollTimer;
    private int _loadMessagesBusy;
    private int _loadMessagesQueued;
    private readonly string? _uiLanguage;
    private string? _statusText;

    public MessengerViewModel(
        IAuthService authService,
        HttpClient httpClient,
        ISettingsService? settingsService = null,
        MessengerDiskCache? diskCache = null)
    {
        _authService = authService;
        _httpClient = httpClient;
        _settingsService = settingsService;
        _uiLanguage = settingsService?.LoadSettings()?.UiLanguage;
        _diskCache = diskCache ?? new MessengerDiskCache();
        _cloudApi = new CloudApiService(httpClient);
        _chatFolders = settingsService != null
            ? new MessengerChatFolderResolver(_cloudApi, settingsService)
            : null;

        Contacts = new ObservableCollection<Contact>();
        Messages = new ObservableCollection<MessageViewModel>();
        GalleryItems = new ObservableCollection<MediaGalleryItemViewModel>();
        SendCommand = new RelayCommand(async _ => await SendMessageAsync(), _ => CanSendNow());
        RefreshContactsCommand = new RelayCommand(async _ => await LoadContactsAsync());
        BackToContactsCommand = new RelayCommand(_ => SelectedContact = null, _ => SelectedContact != null);
        CopyContactIdCommand = new RelayCommand(
            p => CopyContactId(ResolveContactForCopy(p)),
            p => ShareAccountStrings.FormatUserIdForClipboard(ResolveContactForCopy(p)?.Id ?? Guid.Empty) != null);
        OpenContactInformationCommand = new RelayCommand(
            p => OpenContactInformation(ResolveContactForCopy(p)),
            p => TryBuildContactInformationUrl(ResolveContactForCopy(p)) != null);
        InitMediaSendCommands();
        InitMediaRenderCommands();
        InitCloudPickerGalleryUnfurlCommands();

        // Input priority: do not wait behind Background enrichment / Sync deferrals
        // (#messenger-startup-priority / change-then-wait).
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null)
            dispatcher.BeginInvoke(new Action(() => _ = InitializeAsync()), MessengerContactsLoad.ListPaintPriority);
        else
            _ = InitializeAsync();
    }

    public ObservableCollection<Contact> Contacts { get; }
    public ObservableCollection<MessageViewModel> Messages { get; }

    /// <summary>True when the messenger host is narrower than the split breakpoint (~600px).</summary>
    public bool IsCompactLayout
    {
        get => _isCompactLayout;
        set
        {
            if (_isCompactLayout == value) return;
            _isCompactLayout = value;
            OnPropertyChanged();
            NotifyPaneVisibility();
            // Wide layout: open first chat if nothing selected (desktop convenience).
            if (!_isCompactLayout && SelectedContact == null && Contacts.Count > 0)
                SelectedContact = Contacts[0];
        }
    }

    public bool ShowContactsPane => !_isCompactLayout || SelectedContact == null;
    public bool ShowChatPane => !_isCompactLayout || SelectedContact != null;
    public bool ShowChatBackButton => _isCompactLayout && SelectedContact != null;
    public string ChatHeaderTitle => SelectedContact?.Name ?? "Select a chat";

    public string CopyContactIdLabel => ShareAccountStrings.CopyContactIdLabel(_uiLanguage);
    public string ContactInformationLabel => ShareAccountStrings.ContactInformationLabel(_uiLanguage);

    /// <summary>Brief clipboard feedback (Copied); cleared when id unresolved or after delay.</summary>
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

    /// <summary>
    /// True while the open chat is waiting for cache paint or GetEntities after a contact change
    /// (#contacts-responsiveness / change-then-wait).
    /// </summary>
    public bool IsChatLoading
    {
        get => _isChatLoading;
        private set
        {
            if (_isChatLoading == value) return;
            _isChatLoading = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// True while the contacts list is fetching (#messenger-startup-priority / change-then-wait).
    /// </summary>
    public bool IsContactsLoading
    {
        get => _isContactsLoading;
        private set
        {
            if (_isContactsLoading == value) return;
            _isContactsLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowContactsEmpty));
        }
    }

    /// <summary>Honest empty after load finished with zero rows (not silent in-flight empty).</summary>
    public bool ShowContactsEmpty =>
        MessengerContactsLoad.ShouldShowEmpty(IsContactsLoading, Contacts.Count);

    public string ContactsEmptyHint => MessengerContactsLoad.EmptyHint;

    public Contact? SelectedContact
    {
        get => _selectedContact;
        set
        {
            var previousId = _selectedContact?.Id;
            var nextId = value?.Id;
            _selectedContact = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChatHeaderTitle));
            NotifyPaneVisibility();
            CommandManager.InvalidateRequerySuggested();

            var gen = ++_contactSelectGeneration;

            if (value == null)
            {
                StopChatPoll();
                Messages.Clear();
                IsChatLoading = false;
                return;
            }

            // Memory first; cold/sick: hydrate last-good disk page before online prefer.
            var hasCache = _cachedMessages.TryGetValue(value.Id, out var cached);
            if (!hasCache && _authService.CurrentToken != null)
            {
                var fromDisk = TryHydrateMessagesFromDisk(_authService.CurrentToken.UserId, value.Id);
                if (fromDisk != null)
                {
                    cached = fromDisk;
                    hasCache = true;
                }
            }

            // Drop previous partner's thread before any network / hydrate work so chrome can change.
            if (ContactSelection.ShouldClearMessagesForSelection(previousId, nextId) && Messages.Count > 0)
                Messages.Clear();

            // Loading until cache paint or GetEntities finishes — never block selection chrome.
            IsChatLoading = ContactSelection.ShouldShowLoading(nextId, hasCache)
                            || (hasCache && ContactSelection.ShouldClearMessagesForSelection(previousId, nextId));

            var contactId = value.Id;
            var cachedThread = PreferOnlineUiCache.ShouldPaintCacheBeforeOnline(hasCache) ? cached : null;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;

            // Wall-clock leave-Loading from select (not from when Background work eventually runs).
            // Under contacts Refresh / enrich, Background deferral alone pushed clear past ~12s AC.
            if (IsChatLoading)
                _ = EnforceLeaveLoadingBudgetAsync(gen, contactId);

            void ContinueOpenChat()
            {
                if (gen != _contactSelectGeneration || SelectedContact?.Id != contactId)
                    return;

                if (cachedThread != null)
                {
                    ShowMessages(cachedThread);
                    IsChatLoading = false;
                }

                _ = LoadMessagesAsync();
                StartChatPoll();
            }

            // Input (not Background): Messenger tab already visible — do not wait behind EnrichContactNames.
            if (dispatcher != null)
                dispatcher.BeginInvoke(ContinueOpenChat, DispatcherPriority.Input);
            else
                ContinueOpenChat();
        }
    }

    /// <summary>
    /// Forces IsChatLoading off when the interactive budget elapses from contact select,
    /// even if GetEntities has not started yet (UI thread busy / deferred open).
    /// </summary>
    private async Task EnforceLeaveLoadingBudgetAsync(int generation, Guid contactId)
    {
        try
        {
            await Task.Delay(MessengerChatLoad.InteractiveHttpTimeout).ConfigureAwait(true);
        }
        catch
        {
            return;
        }

        if (generation != _contactSelectGeneration) return;
        if (SelectedContact?.Id != contactId) return;
        if (IsChatLoading)
            IsChatLoading = false;
    }
    public string MessageText
    {
        get => _messageText;
        set
        {
            _messageText = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public ICommand SendCommand { get; }
    public ICommand RefreshContactsCommand { get; }
    public ICommand BackToContactsCommand { get; }
    /// <summary>Parameter: <see cref="Contact"/> from list context menu, or null → selected chat.</summary>
    public ICommand CopyContactIdCommand { get; }
    /// <summary>Parameter: <see cref="Contact"/> from list context menu, or null → selected chat.</summary>
    public ICommand OpenContactInformationCommand { get; }

    private Contact? ResolveContactForCopy(object? parameter) =>
        parameter as Contact ?? SelectedContact;

    /// <summary>Public user-home URL for a contact; null when Guid unresolved.</summary>
    public string? TryBuildContactInformationUrl(Contact? contact)
    {
        var id = contact?.Id ?? Guid.Empty;
        if (ShareAccountStrings.FormatUserIdForClipboard(id) == null) return null;
        var settings = _settingsService?.LoadSettings();
        return EntityLinks.Build(
            settings?.PublicSiteBaseUrl,
            id,
            settings?.UiLanguage ?? _uiLanguage);
    }

    private void CopyContactId(Contact? contact)
    {
        var text = ShareAccountStrings.FormatUserIdForClipboard(contact?.Id ?? Guid.Empty);
        if (text == null)
        {
            StatusText = null;
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(text);
            StatusText = ShareAccountStrings.CopiedLabel(_uiLanguage);
            _ = ClearCopyContactIdFeedbackAsync();
        }
        catch
        {
            StatusText = null;
        }
    }

    private void OpenContactInformation(Contact? contact)
    {
        var url = TryBuildContactInformationUrl(contact);
        if (url == null) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = "Could not open browser: " + ex.Message;
        }
    }

    private async Task ClearCopyContactIdFeedbackAsync()
    {
        await Task.Delay(2000);
        if (StatusText == ShareAccountStrings.CopiedLabel(_uiLanguage))
            StatusText = null;
    }

    private void NotifyPaneVisibility()
    {
        OnPropertyChanged(nameof(ShowContactsPane));
        OnPropertyChanged(nameof(ShowChatPane));
        OnPropertyChanged(nameof(ShowChatBackButton));
        LayoutChanged?.Invoke();
    }

    /// <summary>Raised when compact/wide or selected chat changes so the view can retarget columns.</summary>
    public event Action? LayoutChanged;

    /// <summary>Pull contacts + open chat from a SignalR push.</summary>
    public async Task RefreshFromRealtimeAsync()
    {
        await LoadContactsAsync();
        if (SelectedContact != null)
            await LoadMessagesAsync();
    }

    /// <summary>Fast path for <c>message_sent</c>: reload open chat only (no contacts/cloud).</summary>
    public Task RefreshMessagesFromRealtimeAsync() => LoadMessagesAsync();

    /// <summary>Legacy entry used when parameters are unavailable.</summary>
    public Task RefreshMessagesFromRealtimeAsync(string? _) => LoadMessagesAsync();

    private void StartChatPoll()
    {
        StopChatPoll();
        _chatPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _chatPollTimer.Tick += OnChatPollTick;
        _chatPollTimer.Start();
    }

    private void StopChatPoll()
    {
        if (_chatPollTimer == null) return;
        _chatPollTimer.Tick -= OnChatPollTick;
        _chatPollTimer.Stop();
        _chatPollTimer = null;
    }

    private async void OnChatPollTick(object? sender, EventArgs e)
    {
        if (SelectedContact == null) return;
        await LoadMessagesAsync();
    }

    private void ShowMessages(IReadOnlyList<MessageViewModel> messages)
    {
        var withDates = WithDateHeaders(messages);
        if (MessagesLookSame(Messages, withDates))
        {
            foreach (var m in Messages)
            {
                EnsureMessageAttachments(m);
                EnsureMessageUnfurls(m);
            }
            return;
        }
        Messages.Clear();
        foreach (var m in withDates)
        {
            Messages.Add(m);
            EnsureMessageAttachments(m);
            EnsureMessageUnfurls(m);
        }
    }

    private static List<MessageViewModel> WithDateHeaders(IEnumerable<MessageViewModel> messages)
    {
        var result = new List<MessageViewModel>();
        string? lastDay = null;
        foreach (var m in messages.Where(x => !x.IsDateSeparator).OrderBy(x => x.Timestamp).ThenBy(x => x.EntityId))
        {
            m.RefreshLabels();
            var day = m.DayLabel;
            if (!string.IsNullOrEmpty(day) && !string.Equals(day, lastDay, StringComparison.Ordinal))
            {
                result.Add(MessageViewModel.DateHeader(day));
                lastDay = day;
            }
            result.Add(m);
        }
        return result;
    }

    private static bool MessagesLookSame(IReadOnlyList<MessageViewModel> current, IReadOnlyList<MessageViewModel> next)
    {
        if (current.Count != next.Count) return false;
        for (var i = 0; i < next.Count; i++)
        {
            var a = current[i];
            var b = next[i];
            if (a.IsDateSeparator != b.IsDateSeparator || a.IsFromMe != b.IsFromMe || a.Timestamp != b.Timestamp ||
                a.EntityId != b.EntityId || a.DeliveryStatus != b.DeliveryStatus ||
                !string.Equals(a.Text, b.Text, StringComparison.Ordinal) ||
                !string.Equals(a.DayLabel, b.DayLabel, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private void AppendMessageToChat(MessageViewModel message)
    {
        message.RefreshLabels();
        var day = message.DayLabel;
        string? lastDay = null;
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            if (!string.IsNullOrEmpty(Messages[i].DayLabel))
            {
                lastDay = Messages[i].DayLabel;
                break;
            }
        }
        if (!string.IsNullOrEmpty(day) && !string.Equals(day, lastDay, StringComparison.Ordinal))
            Messages.Add(MessageViewModel.DateHeader(day));
        Messages.Add(message);
        EnsureMessageAttachments(message);
        EnsureMessageUnfurls(message);
    }

    private async Task<string> GetUserLoginAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (_userLoginCache.TryGetValue(userId, out var cached)) return cached;
        try
        {
            var response = await _httpClient.GetAsync(
                $"api/Authentication/GetUserById?userId={userId}",
                cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var user = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                if (user.TryGetProperty("login", out var loginEl))
                {
                    var login = loginEl.GetString() ?? userId.ToString();
                    _userLoginCache[userId] = login;
                    return login;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch { }
        return userId.ToString();
    }

    private async Task InitializeAsync()
    {
        if (_authService.CurrentToken == null) return;
        await LoadContactsAsync();
        await RefreshUnreadFromServerAsync();
    }

    private async Task LoadContactsAsync()
    {
        if (_authService.CurrentToken == null) return;

        var gen = ++_contactsLoadGeneration;
        IsContactsLoading = true;
        OnPropertyChanged(nameof(ShowContactsEmpty));
        _ = EnforceLeaveContactsLoadingBudgetAsync(gen);

        var userId = _authService.CurrentToken.UserId;
        // Sick/cold: paint last-good disk contacts before online prefer (never blank wipe).
        if (Contacts.Count == 0)
            TryPaintContactsFromDisk(userId);

        try
        {
            using var loadCts = new CancellationTokenSource(MessengerContactsLoad.InteractiveHttpTimeout);

            // Startup-fast bag: first page + no kid-count enrich so Sync/Notes cannot starve first paint.
            // Prefer online when PreferOnlineUiCache.ShouldRefreshOnlineAfterCachePaint.
            _userContactsId = await MessengerChatLoad.AwaitWithinBudgetAsync(
                UserContactsBagClient.EnsureBagIdStartupFastAsync(_httpClient, userId, loadCts.Token),
                loadCts.Token).ConfigureAwait(true);
            if (gen != _contactsLoadGeneration) return;
            if (!_userContactsId.HasValue)
            {
                if (Contacts.Count == 0)
                {
                    Contacts.Clear();
                    StatusText = MessengerContactsLoad.LoadFailedSoft;
                }
                else
                    StatusText = ShareSheetStrings.OfflineBanner(_uiLanguage);
                return;
            }

            var contactsResponse = await MessengerChatLoad.AwaitWithinBudgetAsync(
                _httpClient.PostAsJsonAsync(
                    "api/Entities/GetEntities",
                    new GetEntitiesContract
                    {
                        ParentIds = new[] { _userContactsId.Value },
                        IncludeValues = true
                    },
                    loadCts.Token),
                loadCts.Token).ConfigureAwait(true);
            if (gen != _contactsLoadGeneration) return;

            var contactsResults = contactsResponse.IsSuccessStatusCode
                ? await MessengerChatLoad.AwaitWithinBudgetAsync(
                    contactsResponse.Content.ReadFromJsonAsync<List<GetEntitiesResult>>(cancellationToken: loadCts.Token),
                    loadCts.Token).ConfigureAwait(true)
                : null;
            if (gen != _contactsLoadGeneration) return;

            if (contactsResults == null)
            {
                if (Contacts.Count == 0)
                    StatusText = MessengerContactsLoad.LoadFailedSoft;
                else
                    StatusText = ShareSheetStrings.OfflineBanner(_uiLanguage);
                return;
            }

            Contacts.Clear();
            var contactUserIds = new List<Guid>();
            foreach (var c in contactsResults)
            {
                var parsed = MessengerContactsLoad.TryParseContactUserId(c.Values, c.Code);
                if (parsed.HasValue)
                    contactUserIds.Add(parsed.Value);
            }

            // Show list immediately (provisional names). Compact starts on the list (no auto-open chat).
            foreach (var id in contactUserIds)
            {
                var provisional = _userLoginCache.TryGetValue(id, out var cached)
                    ? cached
                    : id.ToString();
                Contacts.Add(new Contact { Id = id, Name = provisional });
            }

            // ASAP disk write so next sick-connection paint sees freshest success.
            PersistContactsDiskAsap(userId);

            OnPropertyChanged(nameof(ShowContactsEmpty));
            StatusText = null;

            // Wide layout only: open first chat for convenience.
            if (!_isCompactLayout && SelectedContact == null && Contacts.Count > 0)
                SelectedContact = Contacts[0];

            // Self-row + name enrich + richest-bag reconcile after first paint (Background OK).
            _ = ContinueContactsAfterPaintAsync(userId, contactUserIds, gen);
        }
        catch (Exception ex) when (ApiExceptionHelper.IsExpectedConnectivityFailure(ex))
        {
            if (gen != _contactsLoadGeneration) return;
            if (PreferOnlineUiCache.ShouldKeepCacheOnSoftFail(true, Contacts.Count > 0))
                StatusText = ShareSheetStrings.OfflineBanner(_uiLanguage);
            else if (Contacts.Count == 0)
                StatusText = MessengerContactsLoad.LoadFailedSoft;
        }
        catch (Exception)
        {
            if (gen == _contactsLoadGeneration && Contacts.Count == 0)
                StatusText = MessengerContactsLoad.LoadFailedSoft;
        }
        finally
        {
            if (gen == _contactsLoadGeneration)
            {
                IsContactsLoading = false;
                OnPropertyChanged(nameof(ShowContactsEmpty));
            }
        }
    }

    /// <summary>Self-contact + name enrich + full bag reconcile after provisional list paint.</summary>
    private async Task ContinueContactsAfterPaintAsync(Guid userId, List<Guid> contactUserIds, int generation)
    {
        try
        {
            await EnsureSelfContactAsync(userId).ConfigureAwait(true);
            if (generation != _contactsLoadGeneration) return;
            OnPropertyChanged(nameof(ShowContactsEmpty));

            _ = EnrichContactNamesAsync(contactUserIds);

            // Background richest-bag pick — if different from startup-fast, swap kids once (no re-enter StartupFast).
            var rich = await UserContactsBagClient.EnsureBagIdAsync(_httpClient, userId).ConfigureAwait(true);
            if (generation != _contactsLoadGeneration) return;
            if (rich.HasValue && rich != _userContactsId)
            {
                _userContactsId = rich;
                await ReplaceContactRowsFromBagAsync(rich.Value, userId, generation).ConfigureAwait(true);
            }
        }
        catch
        {
            // Soft: provisional list already shown.
        }
    }

    private async Task ReplaceContactRowsFromBagAsync(Guid bagId, Guid userId, int generation)
    {
        var contactsResponse = await _httpClient.PostAsJsonAsync(
            "api/Entities/GetEntities",
            new GetEntitiesContract
            {
                ParentIds = new[] { bagId },
                IncludeValues = true
            }).ConfigureAwait(true);
        if (generation != _contactsLoadGeneration) return;
        if (!contactsResponse.IsSuccessStatusCode) return;

        var contactsResults = await contactsResponse.Content
            .ReadFromJsonAsync<List<GetEntitiesResult>>().ConfigureAwait(true);
        if (generation != _contactsLoadGeneration) return;

        var contactUserIds = new List<Guid>();
        if (contactsResults != null)
        {
            foreach (var c in contactsResults)
            {
                var parsed = MessengerContactsLoad.TryParseContactUserId(c.Values, c.Code);
                if (parsed.HasValue)
                    contactUserIds.Add(parsed.Value);
            }
        }

        var keepSelected = SelectedContact?.Id;
        Contacts.Clear();
        foreach (var id in contactUserIds)
        {
            var provisional = _userLoginCache.TryGetValue(id, out var cached)
                ? cached
                : id.ToString();
            Contacts.Add(new Contact { Id = id, Name = provisional });
        }

        await EnsureSelfContactAsync(userId).ConfigureAwait(true);
        if (generation != _contactsLoadGeneration) return;
        OnPropertyChanged(nameof(ShowContactsEmpty));

        if (keepSelected.HasValue)
        {
            var match = Contacts.FirstOrDefault(c => c.Id == keepSelected.Value);
            if (match != null)
                SelectedContact = match;
        }
        else if (!_isCompactLayout && SelectedContact == null && Contacts.Count > 0)
            SelectedContact = Contacts[0];

        _ = EnrichContactNamesAsync(contactUserIds);
        PersistContactsDiskAsap(userId);
    }

    private void TryPaintContactsFromDisk(Guid ownerUserId)
    {
        var cached = _diskCache.TryLoadContacts(ownerUserId);
        if (!PreferOnlineUiCache.ShouldPaintCacheBeforeOnline(cached.Count > 0))
            return;

        foreach (var row in cached)
        {
            if (Contacts.Any(c => c.Id == row.Id)) continue;
            _userLoginCache[row.Id] = row.Name;
            Contacts.Add(new Contact { Id = row.Id, Name = row.Name });
        }

        OnPropertyChanged(nameof(ShowContactsEmpty));
    }

    private void PersistContactsDiskAsap(Guid ownerUserId)
    {
        if (Contacts.Count == 0) return;
        _diskCache.SaveContactsAsap(
            ownerUserId,
            Contacts.Select(c => new MessengerDiskCache.CachedContact(c.Id, c.Name)));
    }

    private List<MessageViewModel>? TryHydrateMessagesFromDisk(Guid ownerUserId, Guid partnerUserId)
    {
        var cached = _diskCache.TryLoadMessages(ownerUserId, partnerUserId);
        if (!PreferOnlineUiCache.ShouldPaintCacheBeforeOnline(cached.Count > 0))
            return null;

        var list = cached.Select(m => new MessageViewModel
        {
            EntityId = m.EntityId,
            Text = m.Text,
            SenderName = m.SenderName,
            Timestamp = m.TimestampUtc,
            IsFromMe = m.IsFromMe,
            IsDateSeparator = m.IsDateSeparator,
            TimeLabel = m.TimeLabel,
            DayLabel = m.DayLabel,
            AttachmentIds = m.AttachmentIds.ToArray(),
            PartnerUserId = partnerUserId
        }).ToList();
        _cachedMessages[partnerUserId] = list;
        return list;
    }

    private void PersistMessagesDiskAsap(Guid ownerUserId, Guid partnerUserId, IReadOnlyList<MessageViewModel> list)
    {
        _diskCache.SaveMessagesAsap(
            ownerUserId,
            partnerUserId,
            list.Where(m => !m.IsDateSeparator || m.EntityId != Guid.Empty || !string.IsNullOrEmpty(m.DayLabel))
                .Select(m => new MessengerDiskCache.CachedMessage(
                    m.EntityId,
                    m.Text,
                    m.SenderName,
                    m.Timestamp.Kind == DateTimeKind.Utc ? m.Timestamp : m.Timestamp.ToUniversalTime(),
                    m.IsFromMe,
                    m.IsDateSeparator,
                    m.TimeLabel,
                    m.DayLabel,
                    m.AttachmentIds)));
    }

    private async Task EnforceLeaveContactsLoadingBudgetAsync(int generation)
    {
        try
        {
            await Task.Delay(MessengerContactsLoad.InteractiveHttpTimeout).ConfigureAwait(true);
        }
        catch
        {
            return;
        }

        if (generation != _contactsLoadGeneration) return;
        if (IsContactsLoading)
            IsContactsLoading = false;
    }

    private async Task EnsureSelfContactAsync(Guid userId)
    {
        if (!_userContactsId.HasValue) return;
        var selfResponse = await _httpClient.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
        {
            ParentIds = new[] { _userContactsId.Value, userId }
        });
        var selfResults = await selfResponse.Content.ReadFromJsonAsync<List<GetEntitiesResult>>();
        var selfContact = selfResults?.FirstOrDefault();
        var hasSelf = Contacts.Any(x => x.Id == userId);

        if (selfContact != null && !hasSelf)
        {
            var name = selfContact.Code ?? _authService.CurrentToken?.Login ?? "Me";
            if (string.IsNullOrWhiteSpace(name)) name = "Me";
            Contacts.Insert(0, new Contact { Id = userId, Name = name });
        }
        else if (selfContact == null && !hasSelf)
        {
            var name = _authService.CurrentToken?.Login ?? "Me";
            if (string.IsNullOrWhiteSpace(name)) name = "Me";
            var addSelf = await _httpClient.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
            {
                Code = name,
                ParentIds = new[] { _userContactsId.Value, userId },
                Values = new List<AddValueContract> { new() { Type = TypeOfValue.String, Value = $"userid:{userId}", ParentIds = Array.Empty<Guid>() } }
            });
            if (addSelf.IsSuccessStatusCode)
                Contacts.Insert(0, new Contact { Id = userId, Name = name });
        }
    }

    private async Task EnrichContactNamesAsync(List<Guid> contactUserIds)
    {
        try
        {
            var loginTasks = contactUserIds.Select(id => GetUserLoginAsync(id)).ToArray();
            var names = await Task.WhenAll(loginTasks);
            for (var i = 0; i < contactUserIds.Count; i++)
            {
                var id = contactUserIds[i];
                var name = names[i];
                for (var j = 0; j < Contacts.Count; j++)
                {
                    if (Contacts[j].Id != id) continue;
                    if (!string.Equals(Contacts[j].Name, name, StringComparison.Ordinal))
                        Contacts[j] = new Contact { Id = id, Name = name };
                    break;
                }
            }
        }
        catch { }
    }

    private async Task<Guid> GetOrCreateUserContainer(Guid systemParentId, Guid userId, string code, bool grantWrite = false)
    {
        if (systemParentId == SystemEntities.Sent && userId == _authService.CurrentToken?.UserId && _mySentContainerId.HasValue) return _mySentContainerId.Value;
        if (systemParentId == SystemEntities.Received && userId == _authService.CurrentToken?.UserId && _myReceivedContainerId.HasValue) return _myReceivedContainerId.Value;
        if (systemParentId == SystemEntities.Sent && _partnerSentContainerIds.TryGetValue(userId, out var ps) && ps.HasValue) return ps.Value;
        if (systemParentId == SystemEntities.Received && _partnerReceivedContainerIds.TryGetValue(userId, out var pr) && pr.HasValue) return pr.Value;

        var response = await _httpClient.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
        {
            ParentIds = new[] { systemParentId, userId }
        });
        var results = await response.Content.ReadFromJsonAsync<List<GetEntitiesResult>>();
        if (results?.Any() == true)
        {
            var id = results.First().Id;
            CacheContainer(systemParentId, userId, id);
            return id;
        }

        var addResponse = await _httpClient.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
        {
            Code = code,
            ParentIds = new[] { systemParentId, userId }
        });
        if (!addResponse.IsSuccessStatusCode) return Guid.Empty;
        var addResult = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        var createdId = addResult.GetProperty("id").GetGuid();
        CacheContainer(systemParentId, userId, createdId);

        if (grantWrite && createdId != Guid.Empty)
        {
            await _httpClient.PostAsJsonAsync("api/Entities/AddPermission", new AddPermissionContract
            {
                Id = createdId,
                PermissionForId = userId,
                CanWrite = true
            });
        }
        return createdId;
    }

    private void RemoveOptimisticMessage(Guid partnerUserId, MessageViewModel optimistic)
    {
        if (_cachedMessages.TryGetValue(partnerUserId, out var list))
        {
            list.Remove(optimistic);
            if (list.Count == 0)
                _cachedMessages.Remove(partnerUserId);
            ShowMessages(list);
        }
        else
        {
            Messages.Remove(optimistic);
        }
    }

    private void CacheContainer(Guid systemParentId, Guid userId, Guid id)
    {
        if (systemParentId == SystemEntities.Sent && userId == _authService.CurrentToken?.UserId) _mySentContainerId = id;
        else if (systemParentId == SystemEntities.Received && userId == _authService.CurrentToken?.UserId) _myReceivedContainerId = id;
        else if (systemParentId == SystemEntities.Sent) _partnerSentContainerIds[userId] = id;
        else if (systemParentId == SystemEntities.Received) _partnerReceivedContainerIds[userId] = id;
    }

    private async Task NotifyRealtimeAsync(string commandType, string targetUserId, object? parameters = null)
    {
        try
        {
            await _httpClient.PostAsJsonAsync("api/commands/send", new
            {
                commandType,
                targetUserId,
                parameters
            });
        }
        catch { /* non-fatal */ }
    }

    private async Task LoadMessagesAsync()
    {
        // Coalesce overlapping polls/SignalR/select loads: keep at most one in-flight + one queued.
        if (System.Threading.Interlocked.CompareExchange(ref _loadMessagesBusy, 1, 0) != 0)
        {
            System.Threading.Interlocked.Exchange(ref _loadMessagesQueued, 1);
            return;
        }

        try
        {
            do
            {
                System.Threading.Interlocked.Exchange(ref _loadMessagesQueued, 0);
                await LoadMessagesCoreAsync();
            }
            while (System.Threading.Interlocked.Exchange(ref _loadMessagesQueued, 0) == 1);
        }
        catch (Exception ex) when (MessengerChatLoad.ShouldClearLoadingOnFault(ex))
        {
            // Fire-and-forget select/poll/SignalR: keep cache UI; never leave IsChatLoading stuck
            // (HttpClient timeout, blackholed HTTPS, or unexpected parse) — do not fault the Task.
            System.Diagnostics.Debug.WriteLine(ex);
            IsChatLoading = false;
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _loadMessagesBusy, 0);
            if (System.Threading.Interlocked.Exchange(ref _loadMessagesQueued, 0) == 1)
                _ = LoadMessagesAsync();
        }
    }

    /// <summary>Newest page size for a single conversation (server scopes via ConversationUserIds).</summary>
    public const int ConversationMessagePageSize = 100;

    /// <summary>1 id for self-chat; 2 ids for a pair (order does not matter to the API).</summary>
    public static Guid[] BuildConversationUserIds(Guid myUserId, Guid partnerUserId) =>
        myUserId == partnerUserId
            ? new[] { myUserId }
            : new[] { myUserId, partnerUserId };

    private async Task LoadMessagesCoreAsync()
    {
        if (SelectedContact == null || _authService.CurrentToken == null)
        {
            IsChatLoading = false;
            return;
        }
        var myUserId = _authService.CurrentToken.UserId;
        var partnerUserId = SelectedContact.Id;
        var myStr = myUserId.ToString();

        using var loadCts = new CancellationTokenSource(MessengerChatLoad.InteractiveHttpTimeout);
        try
        {
            try
            {
                var response = await MessengerChatLoad.AwaitWithinBudgetAsync(
                    _httpClient.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
                    {
                        ParentIds = new[] { SystemEntities.Message },
                        IncludeValues = true,
                        Skip = 0,
                        Take = ConversationMessagePageSize,
                        ConversationUserIds = BuildConversationUserIds(myUserId, partnerUserId)
                    }, loadCts.Token),
                    loadCts.Token);
                if (!response.IsSuccessStatusCode)
                    return;
                var all = await MessengerChatLoad.AwaitWithinBudgetAsync(
                    response.Content.ReadFromJsonAsync<List<GetEntitiesResult>>(cancellationToken: loadCts.Token),
                    loadCts.Token);
                if (all == null)
                    return;
                // Contact may have changed while the request was in flight.
                if (SelectedContact?.Id != partnerUserId) return;

                var myLoginTask = GetUserLoginAsync(myUserId, loadCts.Token);
                var partnerLoginTask = GetUserLoginAsync(partnerUserId, loadCts.Token);
                await MessengerChatLoad.AwaitWithinBudgetAsync(
                    Task.WhenAll(myLoginTask, partnerLoginTask),
                    loadCts.Token);
                var myLogin = await myLoginTask;
                var partnerLogin = await partnerLoginTask;

                var list = new List<MessageViewModel>();
                foreach (var msg in all)
                {
                    if (msg.Values == null) continue;
                    string? sender = null;
                    foreach (var val in msg.Values)
                    {
                        if (val is EntityValue ev && ev.Type == "StringValue")
                        {
                            var s = ev.Value?.ToString();
                            if (s != null && s.StartsWith("sender:", StringComparison.OrdinalIgnoreCase))
                                sender = s.Substring(7);
                        }
                    }
                    // Android may store GUIDs with different casing than Guid.ToString().
                    var isFromMe = string.Equals(sender, myStr, StringComparison.OrdinalIgnoreCase);
                    var vm = ParseMessage(msg, isFromMe, isFromMe ? myLogin : partnerLogin);
                    if (vm != null) list.Add(vm);
                }

                if (SelectedContact?.Id != partnerUserId) return;
                list = MergeInFlightLocalMessages(partnerUserId, list);
                // Server returns newest-first; UI list is oldest→newest.
                _cachedMessages[partnerUserId] = list
                    .OrderBy(x => x.Timestamp)
                    .ThenBy(x => x.EntityId)
                    .ToList();
                _loadedContacts.Add(partnerUserId);
                ShowMessages(_cachedMessages[partnerUserId]);
                PersistMessagesDiskAsap(myUserId, partnerUserId, _cachedMessages[partnerUserId]);
                StatusText = null;
                // Receipts / realtime must not keep Chat loading past interactive paint.
                IsChatLoading = false;
                await MarkOpenChatReadAsync(partnerUserId, _cachedMessages[partnerUserId]);
            }
            catch (Exception ex) when (ApiExceptionHelper.IsExpectedConnectivityFailure(ex))
            {
                // Soft-fail: keep cache paint; soft banner when we have last-good.
                System.Diagnostics.Debug.WriteLine(ex);
                if (PreferOnlineUiCache.ShouldKeepCacheOnSoftFail(
                        true,
                        _cachedMessages.ContainsKey(partnerUserId)))
                    StatusText = ShareSheetStrings.OfflineBanner(_uiLanguage);
            }
        }
        finally
        {
            // Always drop Loading for this partner (or if selection cleared) so paint cannot stick
            // when ShowMessages/parse throws after a successful GetEntities.
            if (SelectedContact?.Id == partnerUserId || SelectedContact == null)
                IsChatLoading = false;
        }
    }

    private static MessageViewModel? ParseMessage(GetEntitiesResult message, bool isFromMe, string senderName)
    {
        if (message.Values == null) return null;
        var stringValues = new List<string?>();
        var dateFromValue = DateTime.MinValue;
        var isRead = false;
        foreach (var val in message.Values)
        {
            if (val is not EntityValue ev)
                continue;
            if (ev.Type == "StringValue")
            {
                var s = ev.Value?.ToString() ?? "";
                if (s.Equals("status:read", StringComparison.OrdinalIgnoreCase))
                    isRead = true;
                stringValues.Add(s);
            }
            else if (ev.Type == "DateTimeValue" && ev.Value != null)
            {
                dateFromValue = ParseMessageTimestamp(ev.Value);
            }
        }

        var (text, attachmentIds) = MessageStringValues.Parse(stringValues);
        // Attachment-only messages: keep row when ids present so GUID never becomes body elsewhere.
        if (string.IsNullOrWhiteSpace(text) && attachmentIds.Count == 0) return null;

        // Prefer entity CreationTime: server stores full precision (seconds + milliseconds).
        // DateTime string values are often culture-formatted without fractional seconds.
        var stamp = NormalizeUtc(message.CreationTime);
        if (stamp == default)
            stamp = dateFromValue;
        else if (dateFromValue != default)
        {
            // Keep the more precise instant when both exist (CreationTime usually wins on ticks).
            var createdTicks = stamp.Ticks;
            var valueTicks = NormalizeUtc(dateFromValue).Ticks;
            stamp = createdTicks >= valueTicks ? stamp : NormalizeUtc(dateFromValue);
            // If they match to the second but CreationTime has ms, always keep CreationTime.
            if (Math.Abs(createdTicks - valueTicks) < TimeSpan.TicksPerSecond)
                stamp = NormalizeUtc(message.CreationTime);
        }

        return new MessageViewModel
        {
            EntityId = message.Id,
            Text = text,
            AttachmentIds = attachmentIds,
            Timestamp = stamp,
            IsFromMe = isFromMe,
            SenderName = senderName,
            DeliveryStatus = isFromMe
                ? (isRead ? MessageDeliveryStatus.Read : MessageDeliveryStatus.Sent)
                : MessageDeliveryStatus.Sent
        };
    }

    private static DateTime ParseMessageTimestamp(object raw)
    {
        switch (raw)
        {
            case DateTime dt:
                return NormalizeUtc(dt);
            case DateTimeOffset dto:
                return dto.UtcDateTime;
            case JsonElement je when je.ValueKind == JsonValueKind.String:
            {
                var s = je.GetString();
                if (string.IsNullOrWhiteSpace(s)) return default;
                if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var dto2))
                    return dto2.UtcDateTime;
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var dt2))
                    return NormalizeUtc(dt2);
                if (DateTime.TryParse(s, CultureInfo.CurrentCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt3))
                    return NormalizeUtc(dt3);
                return default;
            }
            case JsonElement je when je.ValueKind == JsonValueKind.Number:
                // Unix ms if large enough, otherwise ignore.
                if (je.TryGetInt64(out var n) && n > 1_000_000_000_000L)
                    return DateTimeOffset.FromUnixTimeMilliseconds(n).UtcDateTime;
                return default;
            default:
            {
                var s = raw.ToString();
                if (string.IsNullOrWhiteSpace(s)) return default;
                if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var dto))
                    return dto.UtcDateTime;
                return default;
            }
        }
    }

    private static DateTime NormalizeUtc(DateTime dt)
    {
        if (dt == default) return default;
        return dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
        };
    }
}

public class Contact : ViewModelBase
{
    private int _unreadCount;
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public int UnreadCount
    {
        get => _unreadCount;
        set
        {
            if (_unreadCount == value) return;
            _unreadCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasUnread));
            OnPropertyChanged(nameof(UnreadLabel));
        }
    }

    public bool HasUnread => UnreadCount > 0;
    public string UnreadLabel => UnreadCount > 99 ? "99+" : UnreadCount.ToString();

    public string Initial
    {
        get
        {
            var t = Name?.Trim();
            if (string.IsNullOrEmpty(t)) return "?";
            return char.ToUpperInvariant(t[0]).ToString();
        }
    }
}

public class MessageViewModel : ViewModelBase
{
    private MessageDeliveryStatus _deliveryStatus = MessageDeliveryStatus.Sent;
    private double _uploadProgress;
    private string? _statusLine;
    private bool _attachmentsBound;
    public Guid EntityId { get; set; }
    public string Text { get; set; } = string.Empty;
    public IReadOnlyList<Guid> AttachmentIds { get; set; } = Array.Empty<Guid>();
    public ObservableCollection<MessageAttachmentViewModel> Attachments { get; } = new();
    public string SenderName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool IsFromMe { get; set; }
    public bool IsDateSeparator { get; set; }
    public string TimeLabel { get; set; } = string.Empty;
    public string DayLabel { get; set; } = string.Empty;

    /// <summary>Local file paths for retry (in-memory only; not restored after restart).</summary>
    public List<PendingAttachmentDraft> PendingLocalAttachments { get; set; } = new();

    /// <summary>Partner user id for this outgoing bubble (retry / cancel / queue).</summary>
    public Guid PartnerUserId { get; set; }

    /// <summary>M8 — visible unfurl cards (403s never appear here).</summary>
    public ObservableCollection<MessageUnfurlCardViewModel> UnfurlCards { get; } = new();

    public bool HasUnfurlCards => UnfurlCards.Count > 0;
    public bool UnfurlsBound { get; set; }

    public void NotifyUnfurlCardsChanged() => OnPropertyChanged(nameof(HasUnfurlCards));

    public bool HasCaption => !string.IsNullOrWhiteSpace(Text);
    public bool HasAttachments => Attachments.Count > 0 || AttachmentIds.Count > 0 || PendingLocalAttachments.Count > 0;
    public bool ShowCaptionText => HasCaption;
    public bool AttachmentsBound
    {
        get => _attachmentsBound;
        set
        {
            if (_attachmentsBound == value) return;
            _attachmentsBound = value;
            OnPropertyChanged();
        }
    }

    public MessageDeliveryStatus DeliveryStatus
    {
        get => _deliveryStatus;
        set
        {
            if (_deliveryStatus == value) return;
            _deliveryStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TicksText));
            OnPropertyChanged(nameof(ShowTicks));
            OnPropertyChanged(nameof(ShowFailedActions));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(ShowUploadProgress));
            OnPropertyChanged(nameof(BubbleOpacity));
        }
    }

    public double UploadProgress
    {
        get => _uploadProgress;
        set
        {
            if (Math.Abs(_uploadProgress - value) < 0.001) return;
            _uploadProgress = value;
            OnPropertyChanged();
        }
    }

    public string? StatusLine
    {
        get => _statusLine;
        set
        {
            if (_statusLine == value) return;
            _statusLine = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowStatusLine));
        }
    }

    public bool ShowStatusLine => !string.IsNullOrEmpty(StatusLine);
    public bool IsFailed => DeliveryStatus == MessageDeliveryStatus.Failed;
    public bool ShowFailedActions => IsFromMe && !IsDateSeparator && DeliveryStatus == MessageDeliveryStatus.Failed;
    public bool ShowUploadProgress =>
        IsFromMe
        && !IsDateSeparator
        && DeliveryStatus is MessageDeliveryStatus.Uploading or MessageDeliveryStatus.Sending;
    public double BubbleOpacity => DeliveryStatus == MessageDeliveryStatus.Failed ? 0.7 : 1.0;

    /// <summary>Ticks only after Sent (M7); Uploading / Sending / Failed hide them.</summary>
    public bool ShowTicks =>
        IsFromMe
        && !IsDateSeparator
        && DeliveryStatus is MessageDeliveryStatus.Sent or MessageDeliveryStatus.Read;

    public string TicksText => DeliveryStatus switch
    {
        MessageDeliveryStatus.Read => "✓✓",
        _ => "✓"
    };

    public static MessageViewModel DateHeader(string label) => new()
    {
        IsDateSeparator = true,
        DayLabel = label,
        Text = label
    };

    public void RefreshLabels()
    {
        if (IsDateSeparator) return;
        TimeLabel = ChatTime.FormatTime(Timestamp);
        DayLabel = ChatTime.FormatDayLabel(Timestamp);
    }
}
