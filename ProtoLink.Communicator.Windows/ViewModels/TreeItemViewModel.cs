using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.ViewModels;

public class TreeItemViewModel : INotifyPropertyChanged
{
    public TreeItemViewModel(FileSystemItem item)
    {
        Item = item;
        Name = item.Name;
        Children = new ObservableCollection<TreeItemViewModel>();
        foreach (var child in item.Children)
            Children.Add(new TreeItemViewModel(child));
    }

    public FileSystemItem Item { get; }
    public string Name { get; set; } = string.Empty;
    public ObservableCollection<TreeItemViewModel> Children { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}
