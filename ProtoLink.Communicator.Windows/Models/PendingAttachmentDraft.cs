using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.Models;

/// <summary>Composer / in-flight attachment before or during media send (in-memory).</summary>
public sealed class PendingAttachmentDraft : INotifyPropertyChanged
{
    private bool _sendAsFile;
    private BitmapImage? _localThumbnail;

    public string LocalPath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string MimeType { get; init; } = "application/octet-stream";
    public long ByteLength { get; init; }

    /// <summary>CloudFile entity id once AddEntity succeeded; reused on Retry. For M3 picks, set before send.</summary>
    public Guid CloudFileEntityId { get; set; }

    /// <summary>
    /// M3 — existing Cloud entity referenced in place (no upload, no chat-folder copy, grant via AddPermission).
    /// </summary>
    public bool IsCloudPick { get; init; }

    /// <summary>True after addFile completed for this draft. Cloud picks are completed without upload.</summary>
    public bool UploadCompleted { get; set; }

    /// <summary>
    /// Composer "Send as file" — forces file-row bubble for the sender; not stored on the message (M6).
    /// </summary>
    public bool SendAsFile
    {
        get => _sendAsFile;
        set
        {
            if (_sendAsFile == value) return;
            _sendAsFile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowSendAsFileBadge));
        }
    }

    public BitmapImage? LocalThumbnail
    {
        get => _localThumbnail;
        set
        {
            if (ReferenceEquals(_localThumbnail, value)) return;
            _localThumbnail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLocalThumbnail));
        }
    }

    public bool HasLocalThumbnail => LocalThumbnail != null;
    public bool IsImageMime => MessengerAttachmentRender.LooksLikeImageMime(MimeType);
    public bool CanToggleSendAsFile => IsImageMime;
    public bool ShowSendAsFileBadge => SendAsFile && IsImageMime;
    public string SizeLabel => MessengerAttachmentRender.FormatByteSize(ByteLength);
    public string TypeGlyph => IsImageMime && !SendAsFile ? "🖼" : "📄";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}