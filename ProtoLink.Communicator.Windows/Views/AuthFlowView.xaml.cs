namespace ProtoLink.Communicator.Windows.Views;

public partial class AuthFlowView : System.Windows.Controls.UserControl
{
    public AuthFlowView()
    {
        InitializeComponent();
    }

    private void RegisterPasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.AuthFlowViewModel vm)
            vm.Password = ((System.Windows.Controls.PasswordBox)sender).Password;
    }

    private void ConfirmPasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.AuthFlowViewModel vm)
            vm.ConfirmPassword = ((System.Windows.Controls.PasswordBox)sender).Password;
    }

    private void LoginPasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.AuthFlowViewModel vm)
            vm.LoginPassword = ((System.Windows.Controls.PasswordBox)sender).Password;
    }
}
