using System.Windows;
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
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select notes root folder",
            UseDescriptionForTitle = true,
            SelectedPath = (DataContext as SettingsViewModel)?.NotesRootPath ?? ""
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        if (DataContext is SettingsViewModel vm)
            vm.NotesRootPath = dlg.SelectedPath;
    }
}
