using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ProtoLink.Communicator.Windows.Services;

public class AuthHandler : System.Net.Http.DelegatingHandler
{
    private readonly ITokenService _tokenService;
    private readonly ILogger<AuthHandler> _logger;
    private readonly Action? _onUnauthorized;

    public AuthHandler(ITokenService tokenService, ILogger<AuthHandler> logger, Action? onUnauthorized = null)
    {
        _tokenService = tokenService;
        _logger = logger;
        _onUnauthorized = onUnauthorized;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _tokenService.LoadToken();
        var hadToken = token != null;
        if (hadToken)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && hadToken)
        {
            _logger.LogWarning("API returned 401 Unauthorized; signing out.");
            try
            {
                _onUnauthorized?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unauthorized callback failed");
            }
        }

        return response;
    }
}
