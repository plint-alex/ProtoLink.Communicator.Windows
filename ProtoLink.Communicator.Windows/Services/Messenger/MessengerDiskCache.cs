using System.IO;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.Services.Messenger;

/// <summary>
/// LocalAppData last-good Messenger contacts + open-chat pages for sick-connection paint.
/// Writes ASAP on successful Api fetch; never blocks UI (best-effort IO).
/// </summary>
public sealed class MessengerDiskCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _root;
    private readonly object _io = new();

    public MessengerDiskCache()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProtoLinkCommunicator",
            "messenger-cache"))
    {
    }

    /// <summary>Test seam: store under a specific directory.</summary>
    public MessengerDiskCache(string rootDir)
    {
        _root = rootDir;
        Directory.CreateDirectory(_root);
    }

    public IReadOnlyList<CachedContact> TryLoadContacts(Guid ownerUserId)
    {
        lock (_io)
        {
            try
            {
                var path = ContactsPath(ownerUserId);
                if (!File.Exists(path)) return Array.Empty<CachedContact>();
                var dto = JsonSerializer.Deserialize<ContactsFile>(File.ReadAllText(path), JsonOptions);
                if (dto?.Contacts == null || dto.OwnerUserId != ownerUserId)
                    return Array.Empty<CachedContact>();
                return dto.Contacts
                    .Where(c => c.Id != Guid.Empty)
                    .Select(c => new CachedContact(c.Id, string.IsNullOrWhiteSpace(c.Name) ? c.Id.ToString("D") : c.Name))
                    .ToList();
            }
            catch
            {
                return Array.Empty<CachedContact>();
            }
        }
    }

    public void SaveContactsAsap(Guid ownerUserId, IEnumerable<CachedContact> contacts)
    {
        if (!PreferOnlineUiCache.ShouldWriteCacheAsap(onlineFetchSucceeded: true)) return;
        lock (_io)
        {
            try
            {
                Directory.CreateDirectory(_root);
                var dto = new ContactsFile
                {
                    OwnerUserId = ownerUserId,
                    SavedUtc = DateTime.UtcNow,
                    Contacts = contacts
                        .Where(c => c.Id != Guid.Empty)
                        .Select(c => new ContactDto { Id = c.Id, Name = c.Name ?? "" })
                        .ToList()
                };
                File.WriteAllText(ContactsPath(ownerUserId), JsonSerializer.Serialize(dto, JsonOptions));
            }
            catch
            {
                // Best-effort; UI must not fail.
            }
        }
    }

    public IReadOnlyList<CachedMessage> TryLoadMessages(Guid ownerUserId, Guid partnerUserId)
    {
        lock (_io)
        {
            try
            {
                var path = MessagesPath(ownerUserId, partnerUserId);
                if (!File.Exists(path)) return Array.Empty<CachedMessage>();
                var dto = JsonSerializer.Deserialize<MessagesFile>(File.ReadAllText(path), JsonOptions);
                if (dto?.Messages == null
                    || dto.OwnerUserId != ownerUserId
                    || dto.PartnerUserId != partnerUserId)
                    return Array.Empty<CachedMessage>();
                return dto.Messages
                    .Where(m => m.EntityId != Guid.Empty || m.IsDateSeparator)
                    .Select(m => new CachedMessage(
                        m.EntityId,
                        m.Text ?? "",
                        m.SenderName ?? "",
                        m.TimestampUtc.Kind == DateTimeKind.Utc
                            ? m.TimestampUtc
                            : m.TimestampUtc.ToUniversalTime(),
                        m.IsFromMe,
                        m.IsDateSeparator,
                        m.TimeLabel ?? "",
                        m.DayLabel ?? "",
                        m.AttachmentIds ?? Array.Empty<Guid>()))
                    .ToList();
            }
            catch
            {
                return Array.Empty<CachedMessage>();
            }
        }
    }

    public void SaveMessagesAsap(Guid ownerUserId, Guid partnerUserId, IEnumerable<CachedMessage> messages)
    {
        if (!PreferOnlineUiCache.ShouldWriteCacheAsap(onlineFetchSucceeded: true)) return;
        lock (_io)
        {
            try
            {
                Directory.CreateDirectory(_root);
                var dto = new MessagesFile
                {
                    OwnerUserId = ownerUserId,
                    PartnerUserId = partnerUserId,
                    SavedUtc = DateTime.UtcNow,
                    Messages = messages.Select(m => new MessageDto
                    {
                        EntityId = m.EntityId,
                        Text = m.Text,
                        SenderName = m.SenderName,
                        TimestampUtc = m.TimestampUtc.Kind == DateTimeKind.Utc
                            ? m.TimestampUtc
                            : m.TimestampUtc.ToUniversalTime(),
                        IsFromMe = m.IsFromMe,
                        IsDateSeparator = m.IsDateSeparator,
                        TimeLabel = m.TimeLabel,
                        DayLabel = m.DayLabel,
                        AttachmentIds = m.AttachmentIds.ToArray()
                    }).ToList()
                };
                File.WriteAllText(
                    MessagesPath(ownerUserId, partnerUserId),
                    JsonSerializer.Serialize(dto, JsonOptions));
            }
            catch
            {
                // Best-effort.
            }
        }
    }

    private string ContactsPath(Guid ownerUserId) =>
        Path.Combine(_root, $"contacts-{ownerUserId:N}.json");

    private string MessagesPath(Guid ownerUserId, Guid partnerUserId) =>
        Path.Combine(_root, $"messages-{ownerUserId:N}-{partnerUserId:N}.json");

    public readonly record struct CachedContact(Guid Id, string Name);

    public readonly record struct CachedMessage(
        Guid EntityId,
        string Text,
        string SenderName,
        DateTime TimestampUtc,
        bool IsFromMe,
        bool IsDateSeparator,
        string TimeLabel,
        string DayLabel,
        IReadOnlyList<Guid> AttachmentIds);

    private sealed class ContactsFile
    {
        public Guid OwnerUserId { get; set; }
        public DateTime SavedUtc { get; set; }
        public List<ContactDto>? Contacts { get; set; }
    }

    private sealed class ContactDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class MessagesFile
    {
        public Guid OwnerUserId { get; set; }
        public Guid PartnerUserId { get; set; }
        public DateTime SavedUtc { get; set; }
        public List<MessageDto>? Messages { get; set; }
    }

    private sealed class MessageDto
    {
        public Guid EntityId { get; set; }
        public string Text { get; set; } = "";
        public string SenderName { get; set; } = "";
        public DateTime TimestampUtc { get; set; }
        public bool IsFromMe { get; set; }
        public bool IsDateSeparator { get; set; }
        public string TimeLabel { get; set; } = "";
        public string DayLabel { get; set; } = "";
        public Guid[]? AttachmentIds { get; set; }
    }
}
