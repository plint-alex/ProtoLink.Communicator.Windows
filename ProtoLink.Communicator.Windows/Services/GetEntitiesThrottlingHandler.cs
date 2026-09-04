using System.Net.Http;
using System.Threading;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// Limits concurrent <c>POST /api/Entities/GetEntities</c> calls app-wide (messenger + cloud + settings share the same gate).
/// Allow 2 so messenger contact load is not stuck behind a long cloud sync page walk.
/// </summary>
public sealed class GetEntitiesThrottlingHandler : DelegatingHandler
{
    private static readonly SemaphoreSlim Gate = new(2, 2);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        if (request.Method == HttpMethod.Post &&
            path.EndsWith("/api/Entities/GetEntities", StringComparison.OrdinalIgnoreCase))
        {
            await Gate.WaitAsync(cancellationToken);
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            finally
            {
                Gate.Release();
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
