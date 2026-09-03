using System.Linq;
using System.Net;
using System.Net.Http;

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

    private static bool ChainHasUnauthorizedHttp(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is not HttpRequestException hre) continue;
            if (hre.StatusCode == HttpStatusCode.Unauthorized)
                return true;
            if (hre.Message.Contains("401", StringComparison.Ordinal) &&
                hre.Message.Contains("HTTP", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
