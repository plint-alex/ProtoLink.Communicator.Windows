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
}
