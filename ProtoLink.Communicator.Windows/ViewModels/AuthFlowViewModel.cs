using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Input;
using ProtoLink.Communicator.Windows.Services;

namespace ProtoLink.Communicator.Windows.ViewModels;

public enum AuthFlowStep
{
    Welcome,
    Email,
    Password,
    CheckEmail,
    Login
}

public class AuthFlowViewModel : ViewModelBase
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IAuthService _authService;
    private AuthFlowStep _step = AuthFlowStep.Welcome;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _confirmPassword = string.Empty;
    private string _loginPassword = string.Empty;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;

    public AuthFlowViewModel(IAuthService authService)
    {
        _authService = authService;
        StartMessagingCommand = new RelayCommand(_ => GoTo(AuthFlowStep.Email), _ => !IsBusy);
        HaveAccountCommand = new RelayCommand(_ => GoTo(AuthFlowStep.Login), _ => !IsBusy);
        EmailNextCommand = new RelayCommand(_ => EmailNext(), _ => !IsBusy);
        PasswordNextCommand = new RelayCommand(async _ => await RegisterAsync(resend: false), _ => !IsBusy);
        ResendCommand = new RelayCommand(async _ => await RegisterAsync(resend: true), _ => !IsBusy);
        ContinueToLoginCommand = new RelayCommand(_ =>
        {
            LoginPassword = Password;
            GoTo(AuthFlowStep.Login);
        }, _ => !IsBusy);
        LoginCommand = new RelayCommand(async _ => await LoginAsync(), _ => !IsBusy);
        BackCommand = new RelayCommand(_ => GoBack(), _ => !IsBusy && Step != AuthFlowStep.Welcome);
        OpenSettingsCommand = new RelayCommand(_ => OnOpenSettings?.Invoke(), _ => !IsBusy);
    }

    public AuthFlowStep Step
    {
        get => _step;
        private set
        {
            if (_step == value) return;
            _step = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWelcome));
            OnPropertyChanged(nameof(IsEmail));
            OnPropertyChanged(nameof(IsPassword));
            OnPropertyChanged(nameof(IsCheckEmail));
            OnPropertyChanged(nameof(IsLogin));
            OnPropertyChanged(nameof(CanGoBack));
            ErrorMessage = string.Empty;
            StatusMessage = string.Empty;
        }
    }

    public bool IsWelcome => Step == AuthFlowStep.Welcome;
    public bool IsEmail => Step == AuthFlowStep.Email;
    public bool IsPassword => Step == AuthFlowStep.Password;
    public bool IsCheckEmail => Step == AuthFlowStep.CheckEmail;
    public bool IsLogin => Step == AuthFlowStep.Login;
    public bool CanGoBack => Step != AuthFlowStep.Welcome;

    public string Email
    {
        get => _email;
        set { _email = value; OnPropertyChanged(); }
    }

    public string Password
    {
        get => _password;
        set { _password = value; OnPropertyChanged(); }
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set { _confirmPassword = value; OnPropertyChanged(); }
    }

    public string LoginPassword
    {
        get => _loginPassword;
        set { _loginPassword = value; OnPropertyChanged(); }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public ICommand StartMessagingCommand { get; }
    public ICommand HaveAccountCommand { get; }
    public ICommand EmailNextCommand { get; }
    public ICommand PasswordNextCommand { get; }
    public ICommand ResendCommand { get; }
    public ICommand ContinueToLoginCommand { get; }
    public ICommand LoginCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand OpenSettingsCommand { get; }

    public event Action? OnLoginSuccess;
    public event Action? OnOpenSettings;

    private void GoTo(AuthFlowStep step) => Step = step;

    private void GoBack()
    {
        Step = Step switch
        {
            AuthFlowStep.Email => AuthFlowStep.Welcome,
            AuthFlowStep.Password => AuthFlowStep.Email,
            AuthFlowStep.CheckEmail => AuthFlowStep.Password,
            AuthFlowStep.Login => AuthFlowStep.Welcome,
            _ => AuthFlowStep.Welcome
        };
    }

    private void EmailNext()
    {
        var email = Email.Trim();
        if (string.IsNullOrWhiteSpace(email) || !EmailRegex.IsMatch(email))
        {
            ErrorMessage = "Enter a valid email address.";
            return;
        }

        Email = email;
        GoTo(AuthFlowStep.Password);
    }

    private async Task RegisterAsync(bool resend)
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        if (!resend)
        {
            if (string.IsNullOrWhiteSpace(Password) || Password.Length < 4)
            {
                ErrorMessage = "Password must be at least 4 characters.";
                return;
            }

            if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
            {
                ErrorMessage = "Passwords do not match.";
                return;
            }
        }

        IsBusy = true;
        try
        {
            var lang = CultureInfo.CurrentUICulture.Name;
            if (string.IsNullOrWhiteSpace(lang)) lang = "en-US";
            var result = await _authService.RegisterAsync(Email, Password, lang);
            if (!result.Success)
            {
                ErrorMessage = !string.IsNullOrWhiteSpace(result.EmailError)
                    ? $"Could not send confirmation email: {result.EmailError}"
                    : (result.Error ?? "Registration failed.");
                return;
            }

            if (Step != AuthFlowStep.CheckEmail)
                GoTo(AuthFlowStep.CheckEmail);
            StatusMessage = resend ? "Confirmation email sent again." : string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoginAsync()
    {
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "Enter your email.";
            return;
        }

        if (string.IsNullOrWhiteSpace(LoginPassword))
        {
            ErrorMessage = "Enter your password.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _authService.LoginAsync(Email.Trim(), LoginPassword);
            if (string.IsNullOrEmpty(result.Error))
                OnLoginSuccess?.Invoke();
            else
                ErrorMessage = result.Error;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
