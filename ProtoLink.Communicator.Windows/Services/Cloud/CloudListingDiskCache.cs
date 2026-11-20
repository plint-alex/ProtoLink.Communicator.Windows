using System.IO;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.Services.Cloud;

/// <summary>
/// LocalAppData last-good Cloud folder listings for sick-connection paint.
/// Writes ASAP on successful listing; lazy deeper expand stays on live tree load.
/// </summary>
public sealed class CloudListingDiskCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _root;
    private readonly object _io = new();

    public CloudListingDiskCache()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProtoLinkCommunicator",
            "cloud-listing-cache"))
    {
    }

    /// <summary>Test seam: store under a specific directory.</summary>
    public CloudListingDiskCache(string rootDir)
    {
        _root = rootDir;
        Directory.CreateDirectory(_root);
    }

    public IReadOnlyList<CachedListingItem> TryLoad(Guid folderId)
    {
        if (folderId == Guid.Empty) return Array.Empty<CachedListingItem>();
        lock (_io)
        {
            try
            {
                var path = PathFor(folderId);
                if (!File.Exists(path)) return Array.Empty<CachedListingItem>();
                var dto = JsonSerializer.Deserialize<ListingFile>(File.ReadAllText(path), JsonOptions);
                if (dto?.Items == null || dto.FolderId != folderId)
                    return Array.Empty<CachedListingItem>();
                return dto.Items
                    .Where(i => i.Id != Guid.Empty)
                    .Select(i => new CachedListingItem(
                        i.Id,
                        string.IsNullOrWhiteSpace(i.Name) ? i.Id.ToString("D") : i.Name,
                        i.IsFolder,
                        i.MimeType,
                        i.UpdateTimeUtc.Kind == DateTimeKind.Utc
                            ? i.UpdateTimeUtc
                            : i.UpdateTimeUtc.ToUniversalTime()))
                    .ToList();
            }
            catch
            {
                return Array.Empty<CachedListingItem>();
            }
        }
    }

    public void SaveAsap(Guid folderId, IEnumerable<CachedListingItem> items)
    {
        if (folderId == Guid.Empty) return;
        if (!PreferOnlineUiCache.ShouldWriteCacheAsap(onlineFetchSucceeded: true)) return;
        lock (_io)
        {
            try
            {
                Directory.CreateDirectory(_root);
                var dto = new ListingFile
                {
                    FolderId = folderId,
                    SavedUtc = DateTime.UtcNow,
                    Items = items
                        .Where(i => i.Id != Guid.Empty)
                        .Select(i => new ItemDto
                        {
                            Id = i.Id,
                            Name = i.Name ?? "",
                            IsFolder = i.IsFolder,
                            MimeType = i.MimeType,
                            UpdateTimeUtc = i.UpdateTimeUtc.Kind == DateTimeKind.Utc
                                ? i.UpdateTimeUtc
                                : i.UpdateTimeUtc.ToUniversalTime()
                        })
                        .ToList()
                };
                File.WriteAllText(PathFor(folderId), JsonSerializer.Serialize(dto, JsonOptions));
            }
            catch
            {
                // Best-effort.
            }
        }
    }

    private string PathFor(Guid folderId) =>
        Path.Combine(_root, $"listing-{folderId:N}.json");

    public readonly record struct CachedListingItem(
        Guid Id,
        string Name,
        bool IsFolder,
        string? MimeType,
        DateTime UpdateTimeUtc);

    private sealed class ListingFile
    {
        public Guid FolderId { get; set; }
        public DateTime SavedUtc { get; set; }
        public List<ItemDto>? Items { get; set; }
    }

    private sealed class ItemDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsFolder { get; set; }
        public string? MimeType { get; set; }
        public DateTime UpdateTimeUtc { get; set; }
    }
}
