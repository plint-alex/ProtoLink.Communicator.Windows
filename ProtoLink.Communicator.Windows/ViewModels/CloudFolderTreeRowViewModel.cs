namespace ProtoLink.Communicator.Windows.ViewModels;

public sealed class CloudFolderTreeRowViewModel : ViewModelBase
{
    private Guid _id;
    private string _name = string.Empty;
    private int _depth;
    private bool _isSelected;

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

    public System.Windows.Thickness IndentMargin => new(8 + Depth * 14, 2, 8, 2);
}
