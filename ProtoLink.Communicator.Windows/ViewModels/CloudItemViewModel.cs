namespace ProtoLink.Communicator.Windows.ViewModels;

public class CloudItemViewModel : ViewModelBase
{
    private Guid _id;
    private string _name = string.Empty;
    private bool _isFolder;
    private bool _isSynced;

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

    public bool IsFolder
    {
        get => _isFolder;
        set { if (_isFolder == value) return; _isFolder = value; OnPropertyChanged(); }
    }

    public bool IsSynced
    {
        get => _isSynced;
        set { if (_isSynced == value) return; _isSynced = value; OnPropertyChanged(); }
    }
}
