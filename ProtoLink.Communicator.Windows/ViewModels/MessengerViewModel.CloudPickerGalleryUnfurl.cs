using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Messenger;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class MessengerViewModel
{
    private static readonly Dictionary<Guid, UnfurlCacheEntry> UnfurlCache = new();

    private bool _isGalleryOpen;
    private string? _galleryEmptyHint;

    public ObservableCollection<MediaGalleryItemViewModel> GalleryItems { get; private set; } = null!;

    public bool IsGalleryOpen
    {
        get => _isGalleryOpen;
        private set
        {
            if (_isGalleryOpen == value) return;
            _isGalleryOpen = value;
            OnPropertyChanged();
        }
    }

    public string? GalleryEmptyHint
    {
        get => _galleryEmptyHint;
        private set
        {
            if (_galleryEmptyHint == value) return;
            _galleryEmptyHint = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowGalleryEmpty));
        }
    }

    public bool ShowGalleryEmpty => IsGalleryOpen && GalleryItems.Count == 0;

    public ICommand OpenMediaGalleryCommand { get; private set; } = null!;
    public ICommand CloseMediaGalleryCommand { get; private set; } = null!;
    public ICommand ActivateGalleryItemCommand { get; private set; } = null!;
    public ICommand OpenUnfurlCommand { get; private set; } = null!;

    /// <summary>Optional: MainWindow may open Cloud natively (E4). Null → browser.</summary>
    public Func<Guid, Task<bool>>? TryOpenEntityInAppAsync { get; set; }

    private void InitCloudPickerGalleryUnfurlCommands()
    {
        OpenMediaGalleryCommand = new RelayCommand(
            _ => OpenMediaGallery(),
            _ => SelectedContact != null);
        CloseMediaGalleryCommand = new RelayCommand(_ => CloseMediaGallery());
        ActivateGalleryItemCommand = new RelayCommand(
            p => _ = ActivateGalleryItemAsync(p as MediaGalleryItemViewModel),
            p => p is MediaGalleryItemViewModel);
        OpenUnfurlCommand = new RelayCommand(
            p => _ = OpenUnfurlAsync(p as MessageUnfurlCardViewModel),
            p => p is MessageUnfurlCardViewModel c && c.IsVisible);
    }

    private void OpenMediaGallery()
    {
        // M9 — scan already-loaded Messages only (no GetEntities / message reload).
        var entries = MessengerMediaGallery.Collect(
            Messages.Select(m => (m.AttachmentIds, m.Timestamp, m.IsFromMe, m.IsDateSeparator)));

        GalleryItems.Clear();
        foreach (var e in entries)
        {
            var tile = new MediaGalleryItemViewModel
            {
                EntityId = e.EntityId,
                MessageTimestamp = e.MessageTimestamp,
                FileName = "…"
            };
            GalleryItems.Add(tile);
            _ = LoadGalleryTileAsync(tile);
        }

        GalleryEmptyHint = GalleryItems.Count == 0 ? "No media in this chat yet" : null;
        IsGalleryOpen = true;
    }

    private void CloseMediaGallery()
    {
        IsGalleryOpen = false;
        GalleryEmptyHint = null;
    }

    private async Task LoadGalleryTileAsync(MediaGalleryItemViewModel tile)
    {
        try
        {
            var entities = await _cloudApi.GetEntitiesByIdsAsync(new[] { tile.EntityId }).ConfigureAwait(true);
            if (entities.Count > 0)
            {
                tile.FileName = CloudApiService.GetDisplayName(entities[0]);
                tile.MimeType = CloudApiService.GetMimeType(entities[0]) ?? MimeTypes.GetMimeType(tile.FileName);
            }
            else
            {
                tile.FileName = $"file-{tile.EntityId:N}";
                tile.MimeType = "application/octet-stream";
            }

            if (!MessengerAttachmentRender.LooksLikeImageMime(tile.MimeType))
            {
                tile.IsImage = false;
                return;
            }

            var longSide = 240;
            if (MessengerThumbnailCache.Shared.TryGet(tile.EntityId, longSide, longSide, out var cached)
                && cached != null)
            {
                tile.Thumbnail = BytesToBitmap(cached);
                tile.IsImage = tile.Thumbnail != null;
                return;
            }

            var img = await _cloudApi.TryGetImageBytesAsync(tile.EntityId, longSide, longSide)
                .ConfigureAwait(true);
            if (img.Status == CloudFileBytesResult.Kind.Success && img.Bytes is { Length: > 0 })
            {
                MessengerThumbnailCache.Shared.Set(tile.EntityId, longSide, longSide, img.Bytes);
                tile.Thumbnail = BytesToBitmap(img.Bytes);
                tile.IsImage = tile.Thumbnail != null;
            }
            else
            {
                tile.IsImage = false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            tile.IsImage = false;
        }
    }

    private async Task ActivateGalleryItemAsync(MediaGalleryItemViewModel? tile)
    {
        if (tile == null) return;
        if (tile.IsImage)
        {
            var stub = new MessageAttachmentViewModel
            {
                EntityId = tile.EntityId,
                FileName = tile.FileName,
                MimeType = tile.MimeType,
                VisualKind = MessengerAttachmentVisualKind.Image,
                Thumbnail = tile.Thumbnail
            };
            CloseMediaGallery();
            await OpenLightboxAsync(stub).ConfigureAwait(true);
            return;
        }

        // Files: same as file-row Download (save, never launch).
        var fileVm = new MessageAttachmentViewModel
        {
            EntityId = tile.EntityId,
            FileName = tile.FileName,
            MimeType = tile.MimeType,
            VisualKind = MessengerAttachmentVisualKind.File
        };
        await DownloadAttachmentAsync(fileVm).ConfigureAwait(true);
    }

    public void EnsureMessageUnfurls(MessageViewModel message)
    {
        if (message.IsDateSeparator || message.UnfurlsBound) return;
        message.UnfurlsBound = true;

        var baseUrl = _settingsService?.LoadSettings().PublicSiteBaseUrl
                      ?? EntityLinks.DefaultPublicSiteBaseUrl;
        var ids = EntityLinkUnfurl.FindEntityIds(message.Text, baseUrl);
        if (ids.Count == 0) return;

        foreach (var id in ids)
        {
            var url = EntityLinks.Build(baseUrl, id);
            var card = new MessageUnfurlCardViewModel { EntityId = id, SourceUrl = url };
            _ = LoadUnfurlCardAsync(message, card);
        }
    }

    private async Task LoadUnfurlCardAsync(MessageViewModel message, MessageUnfurlCardViewModel card)
    {
        try
        {
            if (UnfurlCache.TryGetValue(card.EntityId, out var cached))
            {
                if (!cached.Allowed)
                    return; // 403 — leave plain link only
                ApplyUnfurlCache(card, cached);
                message.UnfurlCards.Add(card);
                message.NotifyUnfurlCardsChanged();
                return;
            }

            var entities = await _cloudApi.GetEntitiesByIdsAsync(new[] { card.EntityId }).ConfigureAwait(true);
            if (entities.Count == 0)
            {
                // Empty GetEntities → no access or gone; do not unfurl (titles must not leak).
                UnfurlCache[card.EntityId] = UnfurlCacheEntry.Denied();
                return;
            }

            var entity = entities[0];
            var title = CloudApiService.GetDisplayName(entity);
            var typeName = entity.Code switch
            {
                CloudEntityCodes.CloudFolder or CloudEntityCodes.CloudRoot => "Folder",
                CloudEntityCodes.CloudFile => "File",
                _ => entity.Code ?? "Entity"
            };
            BitmapImage? thumb = null;
            if (entity.Code == CloudEntityCodes.CloudFile
                && MessengerAttachmentRender.LooksLikeImageMime(CloudApiService.GetMimeType(entity)))
            {
                var img = await _cloudApi.TryGetImageBytesAsync(card.EntityId, 96, 96).ConfigureAwait(true);
                if (img.Status == CloudFileBytesResult.Kind.Success && img.Bytes != null)
                    thumb = BytesToBitmap(img.Bytes);
            }

            var entry = UnfurlCacheEntry.Ok(title, typeName, thumb);
            UnfurlCache[card.EntityId] = entry;
            ApplyUnfurlCache(card, entry);
            message.UnfurlCards.Add(card);
            message.NotifyUnfurlCardsChanged();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            UnfurlCache[card.EntityId] = UnfurlCacheEntry.Denied();
        }
    }

    private static void ApplyUnfurlCache(MessageUnfurlCardViewModel card, UnfurlCacheEntry entry)
    {
        card.Title = entry.Title ?? string.Empty;
        card.TypeName = entry.TypeName ?? string.Empty;
        card.Thumbnail = entry.Thumbnail;
        card.IsVisible = true;
    }

    private async Task OpenUnfurlAsync(MessageUnfurlCardViewModel? card)
    {
        if (card == null || !card.IsVisible) return;
        if (TryOpenEntityInAppAsync != null)
        {
            try
            {
                if (await TryOpenEntityInAppAsync(card.EntityId).ConfigureAwait(true))
                    return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        var baseUrl = _settingsService?.LoadSettings().PublicSiteBaseUrl
                      ?? EntityLinks.DefaultPublicSiteBaseUrl;
        var url = EntityLinks.Build(baseUrl, card.EntityId);
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    public static void ClearUnfurlCacheOnSignOut() => UnfurlCache.Clear();

    private sealed class UnfurlCacheEntry
    {
        public bool Allowed { get; private init; }
        public string? Title { get; private init; }
        public string? TypeName { get; private init; }
        public BitmapImage? Thumbnail { get; private init; }

        public static UnfurlCacheEntry Denied() => new() { Allowed = false };

        public static UnfurlCacheEntry Ok(string title, string typeName, BitmapImage? thumb) => new()
        {
            Allowed = true,
            Title = title,
            TypeName = typeName,
            Thumbnail = thumb
        };
    }
}
