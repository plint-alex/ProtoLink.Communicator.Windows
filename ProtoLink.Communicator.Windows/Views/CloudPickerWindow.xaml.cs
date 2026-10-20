using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProtoLink.Communicator.Windows.ViewModels;

namespace ProtoLink.Communicator.Windows.Views;

public partial class CloudPickerWindow : Window
{
    public CloudPickerSelection? Selection { get; private set; }

    public CloudPickerWindow(CloudPickerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose = result =>
        {
            Selection = result;
            DialogResult = result != null;
            Close();
        };
    }

    private void OnItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: CloudPickerItemViewModel item }) return;
        if (DataContext is CloudPickerViewModel vm && vm.ActivateItemCommand.CanExecute(item))
            vm.ActivateItemCommand.Execute(item);
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Single-click semantics; ignore double-click noise.
        e.Handled = true;
    }
}
