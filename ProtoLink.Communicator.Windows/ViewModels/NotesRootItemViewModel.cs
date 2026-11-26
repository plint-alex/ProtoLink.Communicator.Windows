namespace ProtoLink.Communicator.Windows.ViewModels;

/// <summary>One entry in the Notes multi-root chrome (E11).</summary>
public sealed class NotesRootItemViewModel : ViewModelBase
{
    private bool _isSelected;
    private bool _showNotInCloudCue;
    private string _toolTip = string.Empty;

    public NotesRootItemViewModel(string path, string displayName)
    {
        Path = path;
        DisplayName = displayName;
        _toolTip = path;
    }

    public string Path { get; }
    public string DisplayName { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }

    /// <summary>Muted “Not in Cloud yet” cue when path is outside every Cloud mapping (E2/E11).</summary>
    public bool ShowNotInCloudCue
    {
        get => _showNotInCloudCue;
        set { if (_showNotInCloudCue == value) return; _showNotInCloudCue = value; OnPropertyChanged(); }
    }

    public string ToolTip
    {
        get => _toolTip;
        set { if (_toolTip == value) return; _toolTip = value; OnPropertyChanged(); }
    }
}
