using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace ProtoLink.Communicator.Windows.Services;

internal static class ApiExceptionHelper
{
    /// <summary>True when the exception chain represents an HTTP 401 from the API (logout is handled elsewhere).</summary>
    public static bool IndicatesUnauthorizedHttp(Exception? ex)
    {
        if (ex == null) return false;
        if (ex is AggregateException agg)
            return agg.Flatten().InnerExceptions.Any(ChainHasUnauthorizedHttp);

        return ChainHasUnauthorizedHttp(ex);
    }

    /// <summary>True when the exception chain represents HTTP 403 Forbidden (write ACL refuse, not auth).</summary>
    public static bool IndicatesForbiddenHttp(Exception? ex)
    {
        if (ex == null) return false;
        if (ex is AggregateException agg)
            return agg.Flatten().InnerExceptions.Any(ChainHasForbiddenHttp);

        return ChainHasForbiddenHttp(ex);
    }

    /// <summary>
    /// Transient network / unreachable API failures that background loads and Cloud auto-sync
    /// should soft-fail (keep cache UI; no Background task / Sync error modals).
    /// </summary>
    public static bool IsExpectedConnectivityFailure(Exception? ex)
    {
        if (ex == null) return false;
        if (ex is AggregateException agg)
            return agg.Flatten().InnerExceptions.Any(IsExpectedConnectivityFailure);

        return ChainHasExpectedConnectivityFailure(ex);
    }

    /// <summary>
    /// Known HttpClient content mishandle: response/request stream drained then re-buffered
    /// ("The stream was already consumed. It cannot be read again."). Soft-observe only this
    /// message — do not blanket-swallow all <see cref="InvalidOperationException"/>.
    /// </summary>
    public static bool IsHttpStreamAlreadyConsumedFailure(Exception? ex)
    {
        if (ex == null) return false;
        if (ex is AggregateException agg)
            return agg.Flatten().InnerExceptions.Any(IsHttpStreamAlreadyConsumedFailure);

        return ChainHasHttpStreamAlreadyConsumed(ex);
    }

    /// <summary>
    /// CTS disposed under in-flight WaitAsync/HttpClient (budget abandon race) — soft-observe
    /// so StartupFast escalate cannot surface "Background task error".
    /// </summary>
    public static bool IsCancellationTokenDisposedFailure(Exception? ex)
    {
        if (ex == null) return false;
        if (ex is AggregateException agg)
            return agg.Flatten().InnerExceptions.Any(IsCancellationTokenDisposedFailure);

        return ChainHasCancellationTokenDisposed(ex);
    }

    private static bool ChainHasUnauthorizedHttp(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is not HttpRequestException hre) continue;
            if (hre.StatusCode == HttpStatusCode.Unauthorized)
                return true;
            // EnsureSuccessStatusCode: "… 401 (Unauthorized)." — older paths also said "HTTP 401".
            var msg = hre.Message;
            if (msg.Contains("401", StringComparison.Ordinal) &&
                (msg.Contains("HTTP", StringComparison.OrdinalIgnoreCase)
                 || msg.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private static bool ChainHasForbiddenHttp(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is not HttpRequestException hre) continue;
            if (hre.StatusCode == HttpStatusCode.Forbidden)
                return true;
            var msg = hre.Message;
            if (msg.Contains("403", StringComparison.Ordinal) &&
                (msg.Contains("HTTP", StringComparison.OrdinalIgnoreCase)
                 || msg.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private static bool ChainHasExpectedConnectivityFailure(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is TaskCanceledException or OperationCanceledException)
                return true;
            if (e is SocketException)
                return true;
            if (e is HttpRequestException)
                return true;
            // HttpClient / sockets often wrap resets as IOException without HttpRequestException.
            if (e is IOException && e is not FileNotFoundException and not DirectoryNotFoundException)
                return true;
        }

        return false;
    }

    private static bool ChainHasHttpStreamAlreadyConsumed(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is InvalidOperationException
                && e.Message.Contains("stream was already consumed", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool ChainHasCancellationTokenDisposed(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is not ObjectDisposedException ode) continue;
            // CTS Dispose under in-flight WaitAsync/HttpClient callback registration.
            if (ode.ObjectName != null
                && ode.ObjectName.Contains("CancellationToken", StringComparison.OrdinalIgnoreCase))
                return true;
            if (ode.Message.Contains("CancellationToken", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
