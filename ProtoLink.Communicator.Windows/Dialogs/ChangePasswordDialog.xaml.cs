using System.Windows;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;

namespace ProtoLink.Communicator.Windows.Dialogs;

public partial class ChangePasswordDialog : Window
{
    private readonly IAuthService _authService;
    private readonly string? _uiLanguage;
    private bool _busy;

    public ChangePasswordDialog(IAuthService authService, string? uiLanguage)
    {
        _authService = authService;
        _uiLanguage = uiLanguage;
        InitializeComponent();
        Title = ChangePasswordStrings.Title(uiLanguage);
        CurrentLabel.Text = ChangePasswordStrings.FieldCurrent(uiLanguage);
        NewLabel.Text = ChangePasswordStrings.FieldNew(uiLanguage);
        ConfirmLabel.Text = ChangePasswordStrings.FieldConfirm(uiLanguage);
        SaveButton.Content = ChangePasswordStrings.Submit(uiLanguage);
        CancelButton.Content = ChangePasswordStrings.Cancel(uiLanguage);
    }

    /// <summary>Shows themed dialog; returns true when password was changed successfully.</summary>
    public static bool TryChange(IAuthService authService, string? uiLanguage)
    {
        var dlg = new ChangePasswordDialog(authService, uiLanguage)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        return dlg.ShowDialog() == true;
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => ClearError();

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var current = CurrentBox.Password;
        var next = NewBox.Password;
        var confirm = ConfirmBox.Password;
        var validation = ChangePasswordStrings.Validate(current, next, confirm, _uiLanguage);
        if (validation != null)
        {
            ShowError(validation);
            return;
        }

        _busy = true;
        SaveButton.IsEnabled = false;
        try
        {
            var result = await _authService.ChangePasswordAsync(current, next);
            if (result.Success)
            {
                System.Windows.MessageBox.Show(
                    Owner,
                    $"{ChangePasswordStrings.Success(_uiLanguage)}\n{ChangePasswordStrings.SuccessHint(_uiLanguage)}",
                    ChangePasswordStrings.Title(_uiLanguage),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                DialogResult = true;
                Close();
                return;
            }

            var err = string.IsNullOrWhiteSpace(result.Error)
                ? ChangePasswordStrings.ErrorCurrent(_uiLanguage)
                : result.Error;
            // Prefer stable wrong-current copy when Api message matches.
            if (err.Contains("incorrect", StringComparison.OrdinalIgnoreCase)
                || err.Contains("неверн", StringComparison.OrdinalIgnoreCase))
                err = ChangePasswordStrings.ErrorCurrent(_uiLanguage);
            ShowError(err);
        }
        finally
        {
            _busy = false;
            SaveButton.IsEnabled = true;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = string.Empty;
        ErrorText.Visibility = Visibility.Collapsed;
    }
}
