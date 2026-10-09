using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class MessengerViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly HttpClient _httpClient;
    private Contact? _selectedContact;
    private string _messageText = string.Empty;
    private bool _isCompactLayout;
    private Guid? _userContactsId;
    private Guid? _mySentContainerId;
    private Guid? _myReceivedContainerId;
    private readonly Dictionary<Guid, Guid?> _partnerSentContainerIds = new();
    private readonly Dictionary<Guid, Guid?> _partnerReceivedContainerIds = new();
    private readonly Dictionary<Guid, List<MessageViewModel>> _cachedMessages = new();
    private readonly HashSet<Guid> _loadedContacts = new();
    private readonly Dictionary<Guid, string> _userLoginCache = new();
    private DispatcherTimer? _chatPollTimer;
    private int _loadMessagesBusy;
    private int _loadMessagesQueued;

    public MessengerViewModel(IAuthService authService, HttpClient httpClient)
    {
        _authService = authService;
        _httpClient = httpClient;

        Contacts = new ObservableCollection<Contact>();
        Messages = new ObservableCollection<MessageViewModel>();
        SendCommand = new RelayCommand(async _ => await SendMessageAsync(), _ => SelectedContact != null && !string.IsNullOrWhiteSpace(MessageText));
        RefreshContactsCommand = new RelayCommand(async _ => await LoadContactsAsync());
        BackToContactsCommand = new RelayCommand(_ => SelectedContact = null, _ => SelectedContact != null);

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

    public Contact? SelectedContact
    {
        get => _selectedContact;
        set
        {
            _selectedContact = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChatHeaderTitle));
            NotifyPaneVisibility();
            CommandManager.InvalidateRequerySuggested();
            if (value != null)
            {
                // Show cache immediately, then always refresh from API.
                if (_cachedMessages.TryGetValue(value.Id, out var cached))
                    ShowMessages(cached);
                _ = LoadMessagesAsync();
                StartChatPoll();
            }
            else
            {
                StopChatPoll();
                Messages.Clear();
            }
        }
    }
    public string MessageText { get => _messageText; set { _messageText = value; OnPropertyChanged(); } }
    public ICommand SendCommand { get; }
    public ICommand RefreshContactsCommand { get; }
    public ICommand BackToContactsCommand { get; }

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
        try
        {
            if (SelectedContact == null) return;
            await LoadMessagesAsync();
        }
        catch (Exception)
        {
            // async void Tick: never let transient/network failures become UNHANDLED UI EXCEPTION
        }
    }

    private void ShowMessages(IReadOnlyList<MessageViewModel> messages)
    {
        var withDates = WithDateHeaders(messages);
        if (MessagesLookSame(Messages, withDates)) return;
        Messages.Clear();
        foreach (var m in withDates)
            Messages.Add(m);
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
    }

    private async Task<string> GetUserLoginAsync(Guid userId)
    {
        if (_userLoginCache.TryGetValue(userId, out var cached)) return cached;
        try
        {
            var response = await _httpClient.GetAsync($"api/Authentication/GetUserById?userId={userId}");
            if (response.IsSuccessStatusCode)
            {
                var user = await response.Content.ReadFromJsonAsync<JsonElement>();
                if (user.TryGetProperty("login", out var loginEl))
                {
                    var login = loginEl.GetString() ?? userId.ToString();
                    _userLoginCache[userId] = login;
                    return login;
                }
            }
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

        try
        {
            var userId = _authService.CurrentToken.UserId;
            var response = await _httpClient.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
            {
                ParentIds = new[] { SystemEntities.Contacts, userId }
            });
            var results = await response.Content.ReadFromJsonAsync<List<GetEntitiesResult>>();
            var userContacts = results?.FirstOrDefault();

            if (userContacts == null)
            {
                var addResponse = await _httpClient.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
                {
                    Code = "UserContacts",
                    ParentIds = new[] { SystemEntities.Contacts, userId }
                });
                var addResult = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
                _userContactsId = addResult.GetProperty("id").GetGuid();
            }
            else
                _userContactsId = userContacts.Id;

            if (!_userContactsId.HasValue) return;

            var contactsResponse = await _httpClient.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
            {
                ParentIds = new[] { _userContactsId.Value },
                IncludeValues = true
            });
            var contactsResults = await contactsResponse.Content.ReadFromJsonAsync<List<GetEntitiesResult>>();
            Contacts.Clear();
            var contactUserIds = new List<Guid>();
            if (contactsResults != null)
            {
                foreach (var c in contactsResults)
                {
                    Guid contactUserId = Guid.Empty;
                    if (c.Values != null)
                    {
                        foreach (var val in c.Values)
                        {
                            if (val is EntityValue ev && ev.Type == "StringValue")
                            {
                                var s = ev.Value?.ToString();
                                if (!string.IsNullOrEmpty(s) && s.StartsWith("userid:") && Guid.TryParse(s.AsSpan(7), out var uid))
                                {
                                    contactUserId = uid;
                                    break;
                                }
                            }
                        }
                    }
                    if (contactUserId == Guid.Empty && !string.IsNullOrEmpty(c.Code) && Guid.TryParse(c.Code, out var codeUid))
                        contactUserId = codeUid;
                    if (contactUserId != Guid.Empty)
                        contactUserIds.Add(contactUserId);
                }
            }

            // Show list immediately (provisional names). Compact starts on the list (no auto-open chat).
            foreach (var id in contactUserIds)
            {
                var provisional = _userLoginCache.TryGetValue(id, out var cached)
                    ? cached
                    : id.ToString();
                Contacts.Add(new Contact { Id = id, Name = provisional });
            }

            await EnsureSelfContactAsync(userId);

            // Wide layout only: open first chat for convenience.
            if (!_isCompactLayout && SelectedContact == null && Contacts.Count > 0)
                SelectedContact = Contacts[0];

            // Enrich display names in the background without delaying the chat.
            _ = EnrichContactNamesAsync(contactUserIds);
        }
        catch { }
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
            var loginTasks = contactUserIds.Select(GetUserLoginAsync).ToArray();
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

    private async Task SendMessageAsync()
    {
        if (SelectedContact == null || _authService.CurrentToken == null || string.IsNullOrWhiteSpace(MessageText)) return;
        var myUserId = _authService.CurrentToken.UserId;
        var partnerUserId = SelectedContact.Id;
        var now = DateTime.UtcNow;
        var text = MessageText;

        var optimistic = new MessageViewModel
        {
            Text = text,
            Timestamp = now,
            IsFromMe = true,
            SenderName = await GetUserLoginAsync(myUserId),
            DeliveryStatus = MessageDeliveryStatus.Sending
        };
        if (!_cachedMessages.ContainsKey(partnerUserId)) _cachedMessages[partnerUserId] = new List<MessageViewModel>();
        _cachedMessages[partnerUserId].Add(optimistic);
        AppendMessageToChat(optimistic);
        MessageText = string.Empty;

        var sentTask = GetOrCreateUserContainer(SystemEntities.Sent, myUserId, "Sent");
        var receivedTask = GetOrCreateUserContainer(SystemEntities.Received, partnerUserId, "Received", grantWrite: true);
        await Task.WhenAll(sentTask, receivedTask);
        var sentId = await sentTask;
        var receivedId = await receivedTask;
        if (sentId == Guid.Empty || receivedId == Guid.Empty)
        {
            RemoveOptimisticMessage(partnerUserId, optimistic);
            MessageText = text;
            return;
        }

        // Each value needs a distinct parent "slot" so the API does not dedupe multiple StringValues with empty parents to one row (see DataAccess EntityService.DedupeValuesByParentSlot).
        var addMsg = await _httpClient.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
        {
            Code = "Message",
            ParentIds = new[] { SystemEntities.Message },
            Values = new List<AddValueContract>
            {
                new() { Type = TypeOfValue.String, Value = text, ParentIds = new[] { SystemEntities.Message } },
                new() { Type = TypeOfValue.DateTime, Value = now, ParentIds = new[] { SystemEntities.Message } },
                new() { Type = TypeOfValue.String, Value = $"sender:{myUserId}", ParentIds = new[] { SystemEntities.Sent } },
                new() { Type = TypeOfValue.String, Value = $"receiver:{partnerUserId}", ParentIds = new[] { SystemEntities.Received } }
            },
            Permissions = partnerUserId != myUserId ? new List<AddPermissionContract> { new() { PermissionForId = partnerUserId, CanWrite = false } } : null
        });

        if (!addMsg.IsSuccessStatusCode)
        {
            RemoveOptimisticMessage(partnerUserId, optimistic);
            MessageText = text;
            return;
        }

        try
        {
            var created = await addMsg.Content.ReadFromJsonAsync<JsonElement>();
            if (created.TryGetProperty("id", out var idEl) && idEl.TryGetGuid(out var newId))
                optimistic.EntityId = newId;
        }
        catch { /* keep local id empty */ }
        optimistic.DeliveryStatus = MessageDeliveryStatus.Sent;

        // Real-time: notify peer and other devices of the same user.
        var payload = new { senderId = myUserId.ToString(), messageText = text, timestamp = now };
        await NotifyRealtimeAsync("message_sent", partnerUserId.ToString(), payload);
        if (partnerUserId != myUserId)
            await NotifyRealtimeAsync("message_sent", myUserId.ToString(), payload);
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
                try
                {
                    await LoadMessagesCoreAsync();
                }
                catch (Exception ex) when (IsTransientNetworkFailure(ex))
                {
                    // VPN/Wi‑Fi blip, server reset, etc. Keep cached messages; next poll retries.
                }
            }
            while (System.Threading.Interlocked.Exchange(ref _loadMessagesQueued, 0) == 1);
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _loadMessagesBusy, 0);
            if (System.Threading.Interlocked.Exchange(ref _loadMessagesQueued, 0) == 1)
                _ = LoadMessagesAsync();
        }
    }

    /// <summary>
    /// Connection resets / timeouts during chat poll must not surface as UNHANDLED UI EXCEPTION.
    /// </summary>
    private static bool IsTransientNetworkFailure(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            switch (e)
            {
                case HttpRequestException:
                case IOException:
                case SocketException:
                case TaskCanceledException:
                case OperationCanceledException:
                case TimeoutException:
                    return true;
            }
        }
        return false;
    }

    private async Task LoadMessagesCoreAsync()
    {
        if (SelectedContact == null || _authService.CurrentToken == null) return;
        var myUserId = _authService.CurrentToken.UserId;
        var partnerUserId = SelectedContact.Id;
        var myStr = myUserId.ToString();
        var partnerStr = partnerUserId.ToString();

        // Server defaults Take to 100; message entities are global under SystemEntities.Message,
        // so a small page can omit this conversation entirely.
        var response = await _httpClient.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
        {
            ParentIds = new[] { SystemEntities.Message },
            IncludeValues = true,
            Skip = 0,
            Take = 5000
        });
        if (!response.IsSuccessStatusCode) return;
        var all = await response.Content.ReadFromJsonAsync<List<GetEntitiesResult>>();
        if (all == null) return;
        // Contact may have changed while the request was in flight.
        if (SelectedContact?.Id != partnerUserId) return;

        var sent = new List<GetEntitiesResult>();
        var received = new List<GetEntitiesResult>();
        foreach (var msg in all)
        {
            if (msg.Values == null) continue;
            string? sender = null, receiver = null;
            foreach (var val in msg.Values)
            {
                if (val is EntityValue ev && ev.Type == "StringValue")
                {
                    var s = ev.Value?.ToString();
                    if (s != null)
                    {
                        if (s.StartsWith("sender:", StringComparison.OrdinalIgnoreCase)) sender = s.Substring(7);
                        else if (s.StartsWith("receiver:", StringComparison.OrdinalIgnoreCase)) receiver = s.Substring(9);
                    }
                }
            }
            // Android may store GUIDs with different casing than Guid.ToString().
            if (string.Equals(sender, myStr, StringComparison.OrdinalIgnoreCase)
                && string.Equals(receiver, partnerStr, StringComparison.OrdinalIgnoreCase))
                sent.Add(msg);
            else if (string.Equals(sender, partnerStr, StringComparison.OrdinalIgnoreCase)
                && string.Equals(receiver, myStr, StringComparison.OrdinalIgnoreCase))
                received.Add(msg);
        }

        var list = new List<MessageViewModel>();
        var myLoginTask = GetUserLoginAsync(myUserId);
        var partnerLoginTask = GetUserLoginAsync(partnerUserId);
        await Task.WhenAll(myLoginTask, partnerLoginTask);
        var myLogin = await myLoginTask;
        var partnerLogin = await partnerLoginTask;
        foreach (var msg in sent) { var vm = ParseMessage(msg, true, myLogin); if (vm != null) list.Add(vm); }
        foreach (var msg in received) { var vm = ParseMessage(msg, false, partnerLogin); if (vm != null) list.Add(vm); }

        if (SelectedContact?.Id != partnerUserId) return;
        _cachedMessages[partnerUserId] = list
            .OrderBy(x => x.Timestamp)
            .ThenBy(x => x.EntityId)
            .ToList();
        _loadedContacts.Add(partnerUserId);
        ShowMessages(_cachedMessages[partnerUserId]);
        await MarkOpenChatReadAsync(partnerUserId, _cachedMessages[partnerUserId]);
    }

    private static MessageViewModel? ParseMessage(GetEntitiesResult message, bool isFromMe, string senderName)
    {
        if (message.Values == null) return null;
        var text = "";
        var dateFromValue = DateTime.MinValue;
        var isRead = false;
        foreach (var val in message.Values)
        {
            if (val is EntityValue ev)
            {
                if (ev.Type == "StringValue")
                {
                    var s = ev.Value?.ToString() ?? "";
                    if (s.StartsWith("sender:", StringComparison.OrdinalIgnoreCase) ||
                        s.StartsWith("receiver:", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (s.Equals("status:read", StringComparison.OrdinalIgnoreCase))
                    {
                        isRead = true;
                        continue;
                    }
                    if (s.StartsWith("status:", StringComparison.OrdinalIgnoreCase))
                        continue;
                    text = s;
                }
                else if (ev.Type == "DateTimeValue" && ev.Value != null)
                {
                    dateFromValue = ParseMessageTimestamp(ev.Value);
                }
            }
        }
        if (string.IsNullOrWhiteSpace(text)) return null;

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

public enum MessageDeliveryStatus
{
    Sending,
    Sent,
    Read
}

public class MessageViewModel : ViewModelBase
{
    private MessageDeliveryStatus _deliveryStatus = MessageDeliveryStatus.Sent;
    public Guid EntityId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool IsFromMe { get; set; }
    public bool IsDateSeparator { get; set; }
    public string TimeLabel { get; set; } = string.Empty;
    public string DayLabel { get; set; } = string.Empty;

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
        }
    }

    public bool ShowTicks => IsFromMe && !IsDateSeparator;
    public string TicksText => DeliveryStatus switch
    {
        MessageDeliveryStatus.Sending => "◌",
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
