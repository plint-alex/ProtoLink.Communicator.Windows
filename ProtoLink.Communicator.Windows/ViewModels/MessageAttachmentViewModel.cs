using System.Windows.Media.Imaging;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.ViewModels;

/// <summary>One attachment inside a message bubble (image / file / tombstone).</summary>
public sealed class MessageAttachmentViewModel : ViewModelBase
{
    private MessengerAttachmentVisualKind _visualKind = MessengerAttachmentVisualKind.Loading;
    private BitmapImage? _thumbnail;
    private string _fileName = string.Empty;
    private string? _mimeType;
    private long? _byteLength;
    private string? _statusHint;

    private Guid _entityId;

    public Guid EntityId
    {
        get => _entityId;
        set
        {
            if (_entityId == value) return;
            _entityId = value;
            OnPropertyChanged();
        }
    }

    public bool ForceFileRow { get; init; }

    /// <summary>Local path while uploading / failed optimistic send.</summary>
    public string? LocalPath { get; init; }

    public string FileName
    {
        get => _fileName;
        set
        {
            if (_fileName == value) return;
            _fileName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TypeGlyph));
        }
    }

    public string? MimeType
    {
        get => _mimeType;
        set
        {
            if (_mimeType == value) return;
            _mimeType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TypeGlyph));
        }
    }

    public long? ByteLength
    {
        get => _byteLength;
        set
        {
            if (_byteLength == value) return;
            _byteLength = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SizeLabel));
            OnPropertyChanged(nameof(ShowSize));
        }
    }

    public MessengerAttachmentVisualKind VisualKind
    {
        get => _visualKind;
        set
        {
            if (_visualKind == value) return;
            _visualKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowImage));
            OnPropertyChanged(nameof(ShowFileRow));
            OnPropertyChanged(nameof(ShowTombstone));
            OnPropertyChanged(nameof(ShowLoading));
            OnPropertyChanged(nameof(TombstoneText));
            OnPropertyChanged(nameof(IsNoAccessTombstone));
        }
    }

    public BitmapImage? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (ReferenceEquals(_thumbnail, value)) return;
            _thumbnail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowImage));
        }
    }

    public string? StatusHint
    {
        get => _statusHint;
        set
        {
            if (_statusHint == value) return;
            _statusHint = value;
            OnPropertyChanged();
        }
    }

    public string SizeLabel =>
        ByteLength is long n ? MessengerAttachmentRender.FormatByteSize(n) : string.Empty;

    public bool ShowSize => ByteLength is > 0;
    public bool ShowLoading => VisualKind == MessengerAttachmentVisualKind.Loading;
    public bool ShowImage => VisualKind == MessengerAttachmentVisualKind.Image && Thumbnail != null;
    public bool ShowFileRow => VisualKind == MessengerAttachmentVisualKind.File;
    public bool ShowTombstone =>
        VisualKind is MessengerAttachmentVisualKind.TombstoneRemoved
            or MessengerAttachmentVisualKind.TombstoneNoAccess;

    public bool IsNoAccessTombstone => VisualKind == MessengerAttachmentVisualKind.TombstoneNoAccess;

    public string TombstoneText => VisualKind switch
    {
        MessengerAttachmentVisualKind.TombstoneNoAccess => MessengerAttachmentRender.TombstoneNoAccessEn,
        _ => MessengerAttachmentRender.TombstoneRemovedEn
    };

    public string TypeGlyph
    {
        get
        {
            if (MessengerAttachmentRender.LooksLikeImageMime(MimeType) || LooksLikeImageName(FileName))
                return "🖼";
            var ext = System.IO.Path.GetExtension(FileName)?.ToLowerInvariant();
            return ext switch
            {
                ".pdf" => "📕",
                ".zip" or ".rar" or ".7z" => "🗜",
                _ => "📄"
            };
        }
    }

    private static bool LooksLikeImageName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var ext = System.IO.Path.GetExtension(name);
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";
    }
}
