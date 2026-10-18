using System.Windows;
using ProtoLink.Communicator.Windows.Utilities;
using ProtoLink.Communicator.Windows.ViewModels;

namespace ProtoLink.Communicator.Windows.Views;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnNotesBrowseClick(object sender, RoutedEventArgs e)
    {
        var current = (DataContext as SettingsViewModel)?.NotesRootPath;
        var path = FolderPicker.PickFolder("Select notes root folder", current);
        if (path is null) return;
        if (DataContext is SettingsViewModel vm)
            vm.NotesRootPath = path;
    }
}
