using System.Windows.Media.Imaging;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.ViewModels;

/// <summary>One tile in the M9 per-chat media grid.</summary>
public sealed class MediaGalleryItemViewModel : ViewModelBase
{
    private BitmapImage? _thumbnail;
    private string _fileName = string.Empty;
    private string? _mimeType;
    private bool _isImage;

    public Guid EntityId { get; init; }
    public DateTime MessageTimestamp { get; init; }

    public string FileName
    {
        get => _fileName;
        set
        {
            if (_fileName == value) return;
            _fileName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExtensionLabel));
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

    public bool IsImage
    {
        get => _isImage;
        set
        {
            if (_isImage == value) return;
            _isImage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowThumbnail));
            OnPropertyChanged(nameof(ShowFileTile));
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
            OnPropertyChanged(nameof(ShowThumbnail));
        }
    }

    public bool ShowThumbnail => IsImage && Thumbnail != null;
    public bool ShowFileTile => !ShowThumbnail;

    public string ExtensionLabel
    {
        get
        {
            var ext = System.IO.Path.GetExtension(FileName);
            if (string.IsNullOrEmpty(ext)) return "FILE";
            return ext.TrimStart('.').ToUpperInvariant();
        }
    }

    public string TypeGlyph =>
        MessengerAttachmentRender.LooksLikeImageMime(MimeType) ? "🖼" : "📄";
}
