using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Services;

public interface IAuthService
{
    Task<LoginResult> LoginAsync(string login, string password);
    Task<RegisterResult> RegisterAsync(string email, string password, string lang);
    Task<bool> RefreshTokenAsync();
    void Logout();
    bool IsAuthenticated { get; }
    TokenData? CurrentToken { get; }
}
