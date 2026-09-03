namespace ProtoLink.Communicator.Windows.Views;

public partial class LoginView : System.Windows.Controls.UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    private void PasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.LoginViewModel vm)
            vm.Password = ((System.Windows.Controls.PasswordBox)sender).Password;
    }
}
