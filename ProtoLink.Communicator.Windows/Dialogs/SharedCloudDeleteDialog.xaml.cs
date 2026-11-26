using System.Windows;
using ProtoLink.Communicator.Windows.Navigation;

namespace ProtoLink.Communicator.Windows.Dialogs;

/// <summary>E12: For me only / For all users / Cancel for shared Cloud delete.</summary>
public partial class SharedCloudDeleteDialog : Window
{
    public SharedCloudDeleteChoice Choice { get; private set; } = SharedCloudDeleteChoice.Cancel;

    public SharedCloudDeleteDialog()
    {
        InitializeComponent();
    }

    public static SharedCloudDeleteChoice Show(
        string? lang,
        string name,
        bool isFolder,
        bool canWriteForAll)
    {
        var dlg = new SharedCloudDeleteDialog
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            Title = CloudSharedDeleteStrings.Title(lang),
        };
        dlg.BodyText.Text = CloudSharedDeleteStrings.Body(lang, name, isFolder);
        dlg.ForMeOnlyButton.Content = CloudSharedDeleteStrings.ForMeOnly(lang);
        dlg.ForAllButton.Content = CloudSharedDeleteStrings.ForAll(lang);
        dlg.CancelButton.Content = CloudSharedDeleteStrings.Cancel(lang);
        dlg.ForAllButton.Visibility = canWriteForAll ? Visibility.Visible : Visibility.Collapsed;
        dlg.ShowDialog();
        return dlg.Choice;
    }

    private void OnForMeOnlyClick(object sender, RoutedEventArgs e)
    {
        Choice = SharedCloudDeleteChoice.ForMeOnly;
        DialogResult = true;
        Close();
    }

    private void OnForAllClick(object sender, RoutedEventArgs e)
    {
        Choice = SharedCloudDeleteChoice.ForAll;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Choice = SharedCloudDeleteChoice.Cancel;
        DialogResult = false;
        Close();
    }
}
