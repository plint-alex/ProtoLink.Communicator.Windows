using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Messenger;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class MessengerViewModel
{
    private MessageAttachmentViewModel? _lightboxAttachment;
    private BitmapImage? _lightboxImage;
    private bool _lightboxOpen;
    private bool _lightboxLoading;
    private string? _lightboxError;
    private bool _isDragOverChat;

    public bool IsDragOverChat
    {
        get => _isDragOverChat;
        set
        {
            if (_isDragOverChat == value) return;
            _isDragOverChat = value;
            OnPropertyChanged();
        }
    }

    public bool IsLightboxOpen
    {
        get => _lightboxOpen;
        private set
        {
            if (_lightboxOpen == value) return;
            _lightboxOpen = value;
            OnPropertyChanged();
        }
    }

    public bool IsLightboxLoading
    {
        get => _lightboxLoading;
        private set
        {
            if (_lightboxLoading == value) return;
            _lightboxLoading = value;
            OnPropertyChanged();
        }
    }

    public string? LightboxError
    {
        get => _lightboxError;
        private set
        {
            if (_lightboxError == value) return;
            _lightboxError = value;
            OnPropertyChanged();
        }
    }

    public BitmapImage? LightboxImage
    {
        get => _lightboxImage;
        private set
        {
            _lightboxImage = value;
            OnPropertyChanged();
        }
    }

    public ICommand OpenLightboxCommand { get; private set; } = null!;
    public ICommand CloseLightboxCommand { get; private set; } = null!;
    public ICommand DownloadAttachmentCommand { get; private set; } = null!;
    public ICommand DownloadLightboxCommand { get; private set; } = null!;
    public ICommand ToggleSendAsFileCommand { get; private set; } = null!;
    public ICommand PasteClipboardImageCommand { get; private set; } = null!;

    private void InitMediaRenderCommands()
    {
        OpenLightboxCommand = new RelayCommand(
            p => _ = OpenLightboxAsync(p as MessageAttachmentViewModel),
            p => p is MessageAttachmentViewModel a && a.ShowImage);
        CloseLightboxCommand = new RelayCommand(_ => CloseLightbox());
        DownloadAttachmentCommand = new RelayCommand(
            p => _ = DownloadAttachmentAsync(p as MessageAttachmentViewModel),
            p => p is MessageAttachmentViewModel a
                 && (a.ShowFileRow || a.ShowImage)
                 && a.EntityId != Guid.Empty);
        DownloadLightboxCommand = new RelayCommand(
            _ => _ = DownloadAttachmentAsync(_lightboxAttachment),
            _ => _lightboxAttachment != null && _lightboxAttachment.EntityId != Guid.Empty);
        ToggleSendAsFileCommand = new RelayCommand(
            p =>
            {
                if (p is PendingAttachmentDraft d && d.CanToggleSendAsFile)
                    d.SendAsFile = !d.SendAsFile;
            },
            p => p is PendingAttachmentDraft d && d.CanToggleSendAsFile);
        PasteClipboardImageCommand = new RelayCommand(
            _ => TryPasteClipboardImage(),
            _ => SelectedContact != null);
    }

    /// <summary>Clear private chat thumbs + unfurl cards on sign-out (no disk cache in cycle 1).</summary>
    public static void ClearMediaCachesOnSignOut()
    {
        MessengerThumbnailCache.Shared.Clear();
        ClearUnfurlCacheOnSignOut();
    }

    public void EnsureMessageAttachments(MessageViewModel message)
    {
        if (message.IsDateSeparator || message.AttachmentsBound) return;
        message.AttachmentsBound = true;

        if (message.PendingLocalAttachments.Count > 0 && message.Attachments.Count == 0)
        {
            foreach (var draft in message.PendingLocalAttachments)
            {
                var local = CreateLocalAttachmentVm(draft);
                message.Attachments.Add(local);
                // M3 cloud pick: no local bytes — hydrate thumb / tombstone from API.
                if (draft.IsCloudPick && draft.CloudFileEntityId != Guid.Empty)
                    _ = LoadRemoteAttachmentAsync(local);
            }
        }

        if (message.AttachmentIds.Count == 0) return;

        // Merge remote ids not already represented (after send commits).
        var existing = new HashSet<Guid>(message.Attachments.Where(a => a.EntityId != Guid.Empty).Select(a => a.EntityId));
        foreach (var id in message.AttachmentIds)
        {
            if (existing.Contains(id)) continue;
            var vm = new MessageAttachmentViewModel
            {
                EntityId = id,
                FileName = "…",
                VisualKind = MessengerAttachmentVisualKind.Loading
            };
            message.Attachments.Add(vm);
            _ = LoadRemoteAttachmentAsync(vm);
        }
    }

    private static MessageAttachmentViewModel CreateLocalAttachmentVm(PendingAttachmentDraft draft)
    {
        if (draft.IsCloudPick)
        {
            return new MessageAttachmentViewModel
            {
                EntityId = draft.CloudFileEntityId,
                FileName = draft.FileName,
                MimeType = draft.MimeType,
                ByteLength = draft.ByteLength > 0 ? draft.ByteLength : null,
                ForceFileRow = draft.SendAsFile,
                VisualKind = MessengerAttachmentRender.InitialKind(draft.MimeType, draft.SendAsFile)
            };
        }

        var kind = draft.SendAsFile || !draft.IsImageMime
            ? MessengerAttachmentVisualKind.File
            : MessengerAttachmentVisualKind.Image;
        var vm = new MessageAttachmentViewModel
        {
            EntityId = draft.CloudFileEntityId,
            LocalPath = draft.LocalPath,
            FileName = draft.FileName,
            MimeType = draft.MimeType,
            ByteLength = draft.ByteLength,
            ForceFileRow = draft.SendAsFile,
            VisualKind = kind
        };
        if (kind == MessengerAttachmentVisualKind.Image && draft.LocalThumbnail != null)
            vm.Thumbnail = draft.LocalThumbnail;
        else if (kind == MessengerAttachmentVisualKind.Image)
            vm.Thumbnail = TryLoadLocalBitmap(draft.LocalPath);
        return vm;
    }

    private async Task LoadRemoteAttachmentAsync(MessageAttachmentViewModel vm)
    {
        try
        {
            var entities = await _cloudApi.GetEntitiesByIdsAsync(new[] { vm.EntityId }).ConfigureAwait(true);
            if (entities.Count > 0)
            {
                var entity = entities[0];
                vm.FileName = CloudApiService.GetDisplayName(entity);
                vm.MimeType = CloudApiService.GetMimeType(entity) ?? MimeTypes.GetMimeType(vm.FileName);
            }
            else
            {
                // Empty GetEntities — probe getFile for 403 vs 404.
                var probe = await _cloudApi.TryGetFileBytesAsync(vm.EntityId).ConfigureAwait(true);
                if (probe.Status == CloudFileBytesResult.Kind.NotFound)
                {
                    vm.VisualKind = MessengerAttachmentVisualKind.TombstoneRemoved;
                    return;
                }

                if (probe.Status == CloudFileBytesResult.Kind.NoAccess)
                {
                    vm.VisualKind = MessengerAttachmentVisualKind.TombstoneNoAccess;
                    return;
                }

                vm.FileName = $"file-{vm.EntityId:N}";
                vm.MimeType = "application/octet-stream";
            }

            var forceFile = vm.ForceFileRow;
            var initial = MessengerAttachmentRender.InitialKind(vm.MimeType, forceFile);
            if (initial == MessengerAttachmentVisualKind.File)
            {
                vm.VisualKind = MessengerAttachmentVisualKind.File;
                _ = EnrichFileSizeAsync(vm);
                return;
            }

            await LoadBubbleThumbnailAsync(vm).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            vm.VisualKind = MessengerAttachmentVisualKind.File;
        }
    }

    private async Task EnrichFileSizeAsync(MessageAttachmentViewModel vm)
    {
        try
        {
            var len = await _cloudApi.GetFileContentLengthOrNullAsync(vm.EntityId).ConfigureAwait(true);
            if (len is > 0)
                vm.ByteLength = len;
        }
        catch
        {
            /* size is optional chrome */
        }
    }

    private async Task LoadBubbleThumbnailAsync(MessageAttachmentViewModel vm)
    {
        var scale = 1.0;
        try
        {
            var dpi = VisualTreeHelperDpi();
            scale = dpi / 96.0;
        }
        catch
        {
            scale = 1.0;
        }

        var longSide = (int)Math.Ceiling(MessengerMediaLimits.BubbleImageLongSideLogical * scale);
        var w = longSide;
        var h = longSide;

        if (MessengerThumbnailCache.Shared.TryGet(vm.EntityId, w, h, out var cached) && cached != null)
        {
            var bmp = BytesToBitmap(cached);
            if (bmp != null)
            {
                vm.Thumbnail = bmp;
                vm.VisualKind = MessengerAttachmentVisualKind.Image;
                return;
            }
        }

        var result = await _cloudApi.TryGetImageBytesAsync(vm.EntityId, w, h).ConfigureAwait(true);
        var decoded = result.Status == CloudFileBytesResult.Kind.Success
                      && result.Bytes is { Length: > 0 }
                      && BytesToBitmap(result.Bytes) != null;
        var kind = MessengerAttachmentRender.AfterImageProbe(
            vm.ForceFileRow,
            result.HttpStatus,
            result.ContentType,
            decoded);

        if (kind == MessengerAttachmentVisualKind.Image && result.Bytes != null)
        {
            MessengerThumbnailCache.Shared.Set(vm.EntityId, w, h, result.Bytes);
            vm.Thumbnail = BytesToBitmap(result.Bytes);
            vm.VisualKind = MessengerAttachmentVisualKind.Image;
            return;
        }

        if (kind == MessengerAttachmentVisualKind.TombstoneRemoved
            || kind == MessengerAttachmentVisualKind.TombstoneNoAccess)
        {
            vm.VisualKind = kind;
            return;
        }

        vm.VisualKind = MessengerAttachmentVisualKind.File;
        _ = EnrichFileSizeAsync(vm);
    }

    private async Task OpenLightboxAsync(MessageAttachmentViewModel? attachment)
    {
        if (attachment == null || !attachment.ShowImage) return;
        _lightboxAttachment = attachment;
        IsLightboxOpen = true;
        IsLightboxLoading = true;
        LightboxError = null;
        LightboxImage = null;

        try
        {
            var scale = 1.0;
            try { scale = VisualTreeHelperDpi() / 96.0; } catch { /* ignore */ }
            var longSide = (int)Math.Ceiling(MessengerMediaLimits.LightboxImageLongSideLogical * scale);
            var result = await _cloudApi.TryGetImageBytesAsync(attachment.EntityId, longSide, longSide)
                .ConfigureAwait(true);

            if (result.Status == CloudFileBytesResult.Kind.NoAccess)
            {
                attachment.VisualKind = MessengerAttachmentVisualKind.TombstoneNoAccess;
                CloseLightbox();
                return;
            }

            if (result.Status == CloudFileBytesResult.Kind.NotFound)
            {
                attachment.VisualKind = MessengerAttachmentVisualKind.TombstoneRemoved;
                CloseLightbox();
                return;
            }

            if (result.Status != CloudFileBytesResult.Kind.Success || result.Bytes == null)
            {
                LightboxError = "Couldn't download the file";
                return;
            }

            LightboxImage = BytesToBitmap(result.Bytes);
            if (LightboxImage == null)
                LightboxError = "Couldn't download the file";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            LightboxError = "Couldn't download the file";
        }
        finally
        {
            IsLightboxLoading = false;
        }
    }

    private void CloseLightbox()
    {
        IsLightboxOpen = false;
        LightboxImage = null;
        LightboxError = null;
        IsLightboxLoading = false;
        _lightboxAttachment = null;
    }

    private async Task DownloadAttachmentAsync(MessageAttachmentViewModel? attachment)
    {
        if (attachment == null) return;
        try
        {
            Stream? stream = null;
            string fileName = attachment.FileName;
            if (!string.IsNullOrEmpty(attachment.LocalPath) && File.Exists(attachment.LocalPath))
            {
                stream = File.OpenRead(attachment.LocalPath);
            }
            else if (attachment.EntityId != Guid.Empty)
            {
                var result = await _cloudApi.TryGetFileBytesAsync(attachment.EntityId).ConfigureAwait(true);
                if (result.Status == CloudFileBytesResult.Kind.NotFound)
                {
                    attachment.VisualKind = MessengerAttachmentVisualKind.TombstoneRemoved;
                    return;
                }

                if (result.Status == CloudFileBytesResult.Kind.NoAccess)
                {
                    attachment.VisualKind = MessengerAttachmentVisualKind.TombstoneNoAccess;
                    return;
                }

                if (result.Status != CloudFileBytesResult.Kind.Success || result.Bytes == null)
                {
                    AttachStatusMessage = "Couldn't download the file";
                    return;
                }

                stream = new MemoryStream(result.Bytes);
            }

            if (stream == null) return;

            await using (stream)
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = string.IsNullOrWhiteSpace(fileName) ? "download.bin" : fileName,
                    Title = "Save attachment"
                };
                if (dlg.ShowDialog() != true) return;

                await using var fs = File.Create(dlg.FileName);
                await stream.CopyToAsync(fs).ConfigureAwait(true);
            }
            // Never Process.Start / shell-launch (AT-M17 / AT-M19).
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            AttachStatusMessage = "Couldn't download the file";
        }
    }

    public bool TryPasteClipboardImage()
    {
        AttachStatusMessage = null;
        if (SelectedContact == null) return false;
        if (!System.Windows.Clipboard.ContainsImage()) return false;

        try
        {
            var image = System.Windows.Clipboard.GetImage();
            if (image == null) return false;

            var name = MessengerAttachmentRender.ScreenshotFileName(DateTime.Now);
            var dir = Path.Combine(Path.GetTempPath(), "ProtoLinkMessengerPaste");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, name);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var fs = File.Create(path))
                encoder.Save(fs);

            return TryAddPendingAttachment(path, generatedName: name);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            return false;
        }
    }

    public void AddDroppedFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
            TryAddPendingAttachment(path);
    }

    private static double VisualTreeHelperDpi()
    {
        var src = PresentationSource.FromVisual(System.Windows.Application.Current?.MainWindow!);
        if (src?.CompositionTarget != null)
            return 96.0 * src.CompositionTarget.TransformToDevice.M11;
        return 96.0;
    }

    private static BitmapImage? TryLoadLocalBitmap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.DecodePixelWidth = MessengerMediaLimits.BubbleImageLongSideLogical;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? BytesToBitmap(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
