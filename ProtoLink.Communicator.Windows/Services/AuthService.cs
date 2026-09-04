using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using ProtoLink.Communicator.Windows.Models;
using Newtonsoft.Json;

namespace ProtoLink.Communicator.Windows.Services;

public class AuthService : IAuthService
{
    private readonly HttpClient _httpClient;
    private readonly ITokenService _tokenService;
    private readonly ILogger<AuthService> _logger;
    private TokenData? _currentToken;

    public AuthService(HttpClient httpClient, ITokenService tokenService, ILogger<AuthService> logger)
    {
        _httpClient = httpClient;
        _tokenService = tokenService;
        _logger = logger;
        _currentToken = _tokenService.LoadToken();
    }

    public bool IsAuthenticated => _currentToken != null;
    public TokenData? CurrentToken => _currentToken;

    public async Task<LoginResult> LoginAsync(string login, string password)
    {
        try
        {
            var contract = new LoginContract { Login = login, Password = password };
            var response = await _httpClient.PostAsJsonAsync("api/Authentication/login", contract);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var errorResult = JsonConvert.DeserializeObject<LoginResult>(responseContent);
                return new LoginResult { Error = errorResult?.Error ?? responseContent };
            }

            var result = JsonConvert.DeserializeObject<LoginResult>(responseContent);
            if (result != null && string.IsNullOrEmpty(result.Error))
            {
                _currentToken = new TokenData
                {
                    AccessToken = result.AccessToken,
                    RefreshToken = result.RefreshToken,
                    ExpirationTime = result.ExpirationTime,
                    UserId = result.UserId,
                    Login = result.Login
                };
                _tokenService.SaveToken(_currentToken);
            }
            return result ?? new LoginResult { Error = "Unknown error" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed for {Login}", login);
            return new LoginResult { Error = ex.Message };
        }
    }

    public async Task<bool> RefreshTokenAsync()
    {
        // Prefer in-memory token; fall back to disk (AuthHandler may call us after LoadToken).
        _currentToken ??= _tokenService.LoadToken();
        if (_currentToken?.RefreshToken == null) return false;

        var contract = new RefreshTokenContract
        {
            AccessToken = _currentToken.AccessToken,
            RefreshToken = _currentToken.RefreshToken.Value
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/Authentication/refreshtoken", contract);
            if (!response.IsSuccessStatusCode) return false;

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent) ||
                string.Equals(responseContent.Trim(), "null", StringComparison.OrdinalIgnoreCase))
                return false;

            var result = JsonConvert.DeserializeObject<RefreshTokenResult>(responseContent);
            if (result != null && !string.IsNullOrEmpty(result.AccessToken))
            {
                _currentToken.AccessToken = result.AccessToken;
                _currentToken.RefreshToken = result.RefreshToken;
                _currentToken.ExpirationTime = result.ExpirationTime;
                _tokenService.SaveToken(_currentToken);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh failed");
        }

        return false;
    }

    public void Logout()
    {
        _currentToken = null;
        _tokenService.ClearToken();
    }
}
