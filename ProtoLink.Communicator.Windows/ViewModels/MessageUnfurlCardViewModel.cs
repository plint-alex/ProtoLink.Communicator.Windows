using System.Windows.Media.Imaging;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.ViewModels;

/// <summary>M8 — in-memory unfurl card for a ProtoLink entity link in a message body.</summary>
public sealed class MessageUnfurlCardViewModel : ViewModelBase
{
    private string _title = string.Empty;
    private string _typeName = string.Empty;
    private BitmapImage? _thumbnail;
    private bool _isVisible;

    public Guid EntityId { get; init; }
    public string SourceUrl { get; init; } = string.Empty;

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            OnPropertyChanged();
        }
    }

    public string TypeName
    {
        get => _typeName;
        set
        {
            if (_typeName == value) return;
            _typeName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TypeGlyph));
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
            OnPropertyChanged(nameof(HasThumbnail));
        }
    }

    public bool HasThumbnail => Thumbnail != null;

    /// <summary>False when GetEntity 403'd — bubble keeps plain link text only.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            OnPropertyChanged();
        }
    }

    public string TypeGlyph =>
        TypeName.Equals("Folder", StringComparison.OrdinalIgnoreCase) ? "📁"
        : TypeName.Equals("File", StringComparison.OrdinalIgnoreCase)
            ? (MessengerAttachmentRender.LooksLikeImageMime(null) ? "📄" : "📄")
            : "🔗";
}
