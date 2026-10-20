using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.ViewModels;

/// <summary>One Pattern-1 row in the M3 Choose-from-Cloud picker.</summary>
public sealed class CloudPickerItemViewModel : ViewModelBase
{
    private bool _canSelect = true;
    private string _subtitle = string.Empty;

    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsFolder { get; init; }
    public string? MimeType { get; init; }
    public DateTime UpdateTime { get; init; }
    public string TypeGlyph => IsFolder ? "📁" : (MessengerAttachmentRender.LooksLikeImageMime(MimeType) ? "🖼" : "📄");

    public bool CanSelect
    {
        get => _canSelect;
        set
        {
            if (_canSelect == value) return;
            _canSelect = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDisabled));
            OnPropertyChanged(nameof(RowOpacity));
        }
    }

    public bool IsDisabled => !IsFolder && !CanSelect;

    public double RowOpacity => IsDisabled ? 0.85 : 1.0;

    public string Subtitle
    {
        get => _subtitle;
        set
        {
            if (_subtitle == value) return;
            _subtitle = value;
            OnPropertyChanged();
        }
    }
}
