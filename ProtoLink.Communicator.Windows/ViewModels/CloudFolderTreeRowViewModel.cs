namespace ProtoLink.Communicator.Windows.ViewModels;

public sealed class CloudFolderTreeRowViewModel : ViewModelBase
{
    private Guid _id;
    private string _name = string.Empty;
    private int _depth;
    private bool _isSelected;
    private bool _isExpanded;
    private bool _canExpand;
    private bool _isLoading;

    public Guid Id
    {
        get => _id;
        set { if (_id == value) return; _id = value; OnPropertyChanged(); }
    }

    public string Name
    {
        get => _name;
        set { if (_name == value) return; _name = value; OnPropertyChanged(); }
    }

    public int Depth
    {
        get => _depth;
        set
        {
            if (_depth == value) return;
            _depth = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IndentMargin));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExpandGlyph));
        }
    }

    public bool CanExpand
    {
        get => _canExpand;
        set
        {
            if (_canExpand == value) return;
            _canExpand = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExpandGlyph));
            OnPropertyChanged(nameof(ShowExpandAffordance));
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExpandGlyph));
            OnPropertyChanged(nameof(ShowExpandAffordance));
        }
    }

    /// <summary>Generic folder glyph for Folders tree (AT-CE11).</summary>
    public string FolderGlyph => "📁";

    public bool ShowExpandAffordance => CanExpand || IsLoading;

    public string ExpandGlyph =>
        IsLoading ? "…" : IsExpanded ? "▾" : "▸";

    public System.Windows.Thickness IndentMargin => new(4 + Depth * 14, 2, 8, 2);
}
