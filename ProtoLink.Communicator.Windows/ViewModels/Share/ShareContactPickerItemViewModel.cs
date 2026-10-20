using System.Windows.Input;

namespace ProtoLink.Communicator.Windows.ViewModels.Share;

public sealed class ShareContactPickerItemViewModel : ViewModelBase
{
    private bool _isSelected;
    private readonly Action<ShareContactPickerItemViewModel>? _onToggle;

    public ShareContactPickerItemViewModel(Guid userId, string name, Action<ShareContactPickerItemViewModel>? onToggle = null)
    {
        UserId = userId;
        Name = string.IsNullOrWhiteSpace(name) ? "—" : name.Trim();
        _onToggle = onToggle;
        ToggleCommand = new RelayCommand(_ => IsSelected = !IsSelected);
    }

    public Guid UserId { get; }
    public string Name { get; }
    public ICommand ToggleCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
            _onToggle?.Invoke(this);
        }
    }
}
