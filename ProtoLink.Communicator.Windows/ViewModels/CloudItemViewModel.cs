using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.ViewModels;

public class CloudItemViewModel : ViewModelBase
{
    private Guid _id;
    private string _name = string.Empty;
    private bool _isFolder;
    private bool _isSynced;
    private DateTime _updateTime;
    private string? _mimeType;
    private long? _sizeBytes;
    private string? _subtitleOverride;
    private string? _typeGlyphOverride;
    private bool _isVirtualEntry;
    private bool _isShareRoot;
    private bool _canWriteShare;

    public Guid Id
    {
        get => _id;
        set { if (_id == value) return; _id = value; OnPropertyChanged(); }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SubtitleText));
            OnPropertyChanged(nameof(TypeGlyph));
            OnPropertyChanged(nameof(TypeDisplay));
        }
    }

    public bool IsFolder
    {
        get => _isFolder;
        set
        {
            if (_isFolder == value) return;
            _isFolder = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SubtitleText));
            OnPropertyChanged(nameof(TypeGlyph));
            OnPropertyChanged(nameof(TypeDisplay));
            OnPropertyChanged(nameof(SizeDisplay));
        }
    }

    public bool IsSynced
    {
        get => _isSynced;
        set
        {
            if (_isSynced == value) return;
            _isSynced = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SubtitleText));
            OnPropertyChanged(nameof(SyncBadgeText));
        }
    }

    public DateTime UpdateTime
    {
        get => _updateTime;
        set
        {
            if (_updateTime == value) return;
            _updateTime = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ModifiedDisplay));
        }
    }

    /// <summary>CloudFile mime value (MimeTypeId slot); unused for folders.</summary>
    public string? MimeType
    {
        get => _mimeType;
        set
        {
            if (_mimeType == value) return;
            _mimeType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SubtitleText));
            OnPropertyChanged(nameof(TypeGlyph));
            OnPropertyChanged(nameof(TypeDisplay));
        }
    }

    /// <summary>
    /// Cheap size for Explorer details (local FileInfo / sync meta). Null → SizeDisplay em-dash.
    /// Never set from N+1 content-length probes.
    /// </summary>
    public long? SizeBytes
    {
        get => _sizeBytes;
        set
        {
            if (_sizeBytes == value) return;
            _sizeBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SizeDisplay));
        }
    }

    /// <summary>Hub / Shared-with-me synthetic row — not a real CloudFolder for Map/mutations.</summary>
    public bool IsVirtualEntry
    {
        get => _isVirtualEntry;
        set { if (_isVirtualEntry == value) return; _isVirtualEntry = value; OnPropertyChanged(); }
    }

    public bool IsShareRoot
    {
        get => _isShareRoot;
        set
        {
            if (_isShareRoot == value) return;
            _isShareRoot = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SubtitleText));
        }
    }

    public bool CanWriteShare
    {
        get => _canWriteShare;
        set
        {
            if (_canWriteShare == value) return;
            _canWriteShare = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SubtitleText));
        }
    }

    public string? SubtitleOverride
    {
        get => _subtitleOverride;
        set
        {
            if (_subtitleOverride == value) return;
            _subtitleOverride = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SubtitleText));
        }
    }

    public string? TypeGlyphOverride
    {
        get => _typeGlyphOverride;
        set
        {
            if (_typeGlyphOverride == value) return;
            _typeGlyphOverride = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TypeGlyph));
        }
    }

    public string TypeGlyph
    {
        get
        {
            if (!string.IsNullOrEmpty(TypeGlyphOverride))
                return TypeGlyphOverride!;
            if (IsFolder)
                return "📁";
            return CloudFileTypeCue.TypeGlyph(MimeType, Name);
        }
    }

    /// <summary>Muted subtitle: type · Synced (when mapped), or Shared-with-me caption.</summary>
    public string SubtitleText
    {
        get
        {
            if (!string.IsNullOrEmpty(SubtitleOverride))
                return SubtitleOverride!;
            var type = TypeDisplay;
            return IsSynced ? $"{type} · Synced" : type;
        }
    }

    /// <summary>Explorer Type column: Folder or mime/ext cue (ignores Shared caption override).</summary>
    public string TypeDisplay =>
        IsFolder ? "Folder" : CloudFileTypeCue.ShortLabel(MimeType, Name);

    /// <summary>Explorer Size column: cheap length or em-dash (AT-CE2).</summary>
    public string SizeDisplay =>
        IsFolder || IsVirtualEntry ? CloudItemSizeCue.Unknown : CloudItemSizeCue.Format(SizeBytes);

    public string ModifiedDisplay =>
        IsVirtualEntry ? string.Empty : ChatTime.FormatListMeta(UpdateTime);

    public string SyncBadgeText => IsSynced ? "✓" : string.Empty;
}
