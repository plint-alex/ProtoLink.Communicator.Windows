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
    private readonly Func<Task<bool>>? _tryRefresh;
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);

    public AuthHandler(
        ITokenService tokenService,
        ILogger<AuthHandler> logger,
        Action? onUnauthorized = null,
        Func<Task<bool>>? tryRefresh = null)
    {
        _tokenService = tokenService;
        _logger = logger;
        _onUnauthorized = onUnauthorized;
        _tryRefresh = tryRefresh;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _tokenService.LoadToken();
        var hadToken = token != null;
        if (hadToken)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        // Buffer body up front so we can retry after a silent token refresh.
        byte[]? bodyBytes = null;
        if (request.Content != null)
        {
            bodyBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var buffered = new ByteArrayContent(bodyBytes);
            foreach (var header in request.Content.Headers)
                buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
            request.Content = buffered;
        }

        var response = await base.SendAsync(request, cancellationToken);

        // Only 401, or legacy 403 "User ID not found" (expired token past soft IpPolicy).
        var isUnauthorized = response.StatusCode == HttpStatusCode.Unauthorized;
        var isLegacyAuthGap = false;
        if (!isUnauthorized && response.StatusCode == HttpStatusCode.Forbidden && hadToken && !IsAuthEndpoint(request))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            isLegacyAuthGap = body.Contains("User ID not found", StringComparison.OrdinalIgnoreCase);
            response.Content = new StringContent(body, System.Text.Encoding.UTF8, response.Content.Headers.ContentType?.MediaType ?? "application/json");
            if (!isLegacyAuthGap)
                return response;
        }
        else if (!isUnauthorized || !hadToken || IsAuthEndpoint(request))
        {
            return response;
        }

        // Expired JWT or single-session refresh overwrite: try refresh before signing out.
        // AuthService uses an HttpClient without this handler, so refresh will not recurse.
        if (_tryRefresh != null)
        {
            await RefreshGate.WaitAsync(cancellationToken);
            try
            {
                if (await _tryRefresh())
                {
                    _logger.LogInformation("API returned {Status}; refreshed session and keeping login.", (int)response.StatusCode);
                    response.Dispose();
                    var retry = CreateRetryRequest(request, bodyBytes);
                    var refreshed = _tokenService.LoadToken();
                    if (refreshed != null)
                        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
                    return await base.SendAsync(retry, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Token refresh after {Status} failed", (int)response.StatusCode);
            }
            finally
            {
                RefreshGate.Release();
            }
        }

        // Only clear the session for real auth failures (not ordinary 403 Forbidden).
        if (isUnauthorized || isLegacyAuthGap)
        {
            _logger.LogWarning("API auth failed ({Status}); signing out.", (int)response.StatusCode);
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

    private static bool IsAuthEndpoint(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        return path.Contains("/api/Authentication/", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpRequestMessage CreateRetryRequest(HttpRequestMessage request, byte[]? bodyBytes)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (bodyBytes != null)
        {
            clone.Content = new ByteArrayContent(bodyBytes);
            if (request.Content != null)
            {
                foreach (var header in request.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
