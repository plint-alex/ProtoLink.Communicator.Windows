using System.Windows;

namespace ProtoLink.Communicator.Windows.Dialogs;

public partial class NotesExternalChangeDialog : Window
{
    public NotesExternalChangeDialog()
    {
        InitializeComponent();
    }

    public event EventHandler? ReloadFromDiskRequested;
    public event EventHandler? KeepEditsRequested;

    private void OnReloadClick(object sender, RoutedEventArgs e)
    {
        ReloadFromDiskRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void OnKeepClick(object sender, RoutedEventArgs e)
    {
        KeepEditsRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }
}
