using System.Media;
using System.Net.Http.Json;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class MessengerViewModel
{
    private readonly MessengerReadStore _readStore = new();
    private readonly HashSet<Guid> _readReceiptNotified = new();
    private int _totalUnread;

    public int TotalUnread
    {
        get => _totalUnread;
        private set
        {
            if (_totalUnread == value) return;
            _totalUnread = value;
            OnPropertyChanged();
            UnreadChanged?.Invoke(value);
        }
    }

    /// <summary>Raised when total unread count changes (taskbar badge).</summary>
    public event Action<int>? UnreadChanged;

    /// <summary>True when the main window is active/foreground (set by MainWindow).</summary>
    public bool IsAppForeground { get; set; } = true;

    public async Task HandleRealtimeCommandAsync(string? commandType, JsonElement? parameters)
    {
        if (string.Equals(commandType, "message_read", StringComparison.OrdinalIgnoreCase))
        {
            await HandleMessageReadAsync(parameters);
            return;
        }

        if (string.Equals(commandType, "message_sent", StringComparison.OrdinalIgnoreCase))
        {
            await HandleIncomingMessageSentAsync(parameters);
            return;
        }

        await RefreshFromRealtimeAsync();
    }

    private async Task HandleIncomingMessageSentAsync(JsonElement? parameters)
    {
        Guid? senderId = null;
        string? preview = null;
        if (parameters is { ValueKind: JsonValueKind.Object } p)
        {
            if (p.TryGetProperty("senderId", out var s) || p.TryGetProperty("SenderId", out s))
            {
                var raw = s.GetString();
                if (Guid.TryParse(raw, out var g)) senderId = g;
            }
            if (p.TryGetProperty("messageText", out var t) || p.TryGetProperty("MessageText", out t))
                preview = t.GetString();
        }

        var myId = _authService.CurrentToken?.UserId;
        var fromOther = senderId.HasValue && myId.HasValue && senderId.Value != myId.Value;
        var chatOpen = SelectedContact != null && senderId.HasValue && SelectedContact.Id == senderId.Value;
        if (fromOther && (!chatOpen || !IsAppForeground))
        {
            try { SystemSounds.Asterisk.Play(); } catch { /* ignore */ }
        }

        if (SelectedContact != null)
            await LoadMessagesAsync();
        else
            await RefreshUnreadFromServerAsync();

        if (fromOther && !chatOpen)
            await RefreshUnreadFromServerAsync();
    }

    private async Task HandleMessageReadAsync(JsonElement? parameters)
    {
        if (_authService.CurrentToken == null || parameters is not { ValueKind: JsonValueKind.Object } p)
            return;

        var ids = new List<Guid>();
        if (p.TryGetProperty("messageIds", out var arr) || p.TryGetProperty("MessageIds", out arr))
        {
            if (arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    var s = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
                    if (Guid.TryParse(s, out var id)) ids.Add(id);
                }
            }
        }
        if (ids.Count == 0) return;

        foreach (var id in ids)
            await PersistStatusReadAsync(id);

        foreach (var list in _cachedMessages.Values)
        {
            foreach (var m in list)
            {
                if (m.IsFromMe && ids.Contains(m.EntityId))
                    m.DeliveryStatus = MessageDeliveryStatus.Read;
            }
        }
        foreach (var m in Messages)
        {
            if (m.IsFromMe && ids.Contains(m.EntityId))
                m.DeliveryStatus = MessageDeliveryStatus.Read;
        }
    }

    private async Task PersistStatusReadAsync(Guid messageId)
    {
        try
        {
            await _httpClient.PostAsJsonAsync($"api/Entities/AddValue/{messageId}", new AddValueContract
            {
                Type = TypeOfValue.String,
                Value = "status:read",
                ParentIds = new[] { SystemEntities.Message }
            });
        }
        catch { /* non-fatal */ }
    }

    private async Task MarkOpenChatReadAsync(Guid partnerUserId, IReadOnlyList<MessageViewModel> messages)
    {
        _readStore.MarkOpened(partnerUserId, DateTime.UtcNow);
        UpdateContactUnread(partnerUserId, 0);
        RecalcTotalUnread();

        var incomingIds = messages
            .Where(m => !m.IsDateSeparator && !m.IsFromMe && m.EntityId != Guid.Empty)
            .Select(m => m.EntityId)
            .Where(id => _readReceiptNotified.Add(id))
            .ToList();
        if (incomingIds.Count == 0) return;

        var payload = new
        {
            messageIds = incomingIds.Select(id => id.ToString()).ToArray(),
            peerId = _authService.CurrentToken?.UserId.ToString()
        };
        await NotifyRealtimeAsync("message_read", partnerUserId.ToString(), payload);
    }

    public async Task RefreshUnreadFromServerAsync()
    {
        if (_authService.CurrentToken == null) return;
        var myUserId = _authService.CurrentToken.UserId;
        var myStr = myUserId.ToString();
        try
        {
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

            var counts = new Dictionary<Guid, int>();
            foreach (var c in Contacts)
                counts[c.Id] = 0;

            foreach (var msg in all)
            {
                if (msg.Values == null) continue;
                string? sender = null, receiver = null;
                foreach (var val in msg.Values)
                {
                    if (val is not EntityValue ev || ev.Type != "StringValue") continue;
                    var s = ev.Value?.ToString();
                    if (s == null) continue;
                    if (s.StartsWith("sender:", StringComparison.OrdinalIgnoreCase)) sender = s.Substring(7);
                    else if (s.StartsWith("receiver:", StringComparison.OrdinalIgnoreCase)) receiver = s.Substring(9);
                }
                if (!string.Equals(receiver, myStr, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(sender))
                    continue;
                if (!Guid.TryParse(sender, out var senderId) || senderId == myUserId) continue;
                var stamp = NormalizeUtc(msg.CreationTime);
                var opened = _readStore.GetOpenedUtc(senderId);
                if (stamp > opened)
                {
                    counts.TryGetValue(senderId, out var n);
                    counts[senderId] = n + 1;
                }
            }

            foreach (var c in Contacts)
                UpdateContactUnread(c.Id, counts.TryGetValue(c.Id, out var n) ? n : 0);
            RecalcTotalUnread();
        }
        catch { /* ignore */ }
    }

    private void UpdateContactUnread(Guid contactId, int count)
    {
        var c = Contacts.FirstOrDefault(x => x.Id == contactId);
        if (c != null) c.UnreadCount = count;
    }

    private void RecalcTotalUnread()
    {
        TotalUnread = Contacts.Sum(c => c.UnreadCount);
    }
}
