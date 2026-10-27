using System.Net.Http;
using System.Threading;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// Limits concurrent <c>POST …/Entities/GetEntities</c> on the <see cref="Lane.Sync"/> lane only
/// (Cloud mapped sync / Notes share API). Interactive Messenger/Settings clients use
/// <see cref="Lane.Interactive"/> and are never blocked by sync — otherwise Add contact and
/// Messenger contact load starve behind long sync page walks (shared SemaphoreSlim of 2).
/// </summary>
public sealed class GetEntitiesThrottlingHandler : DelegatingHandler
{
    public enum Lane
    {
        /// <summary>UI / Messenger / Settings — never waits on the sync gate.</summary>
        Interactive,

        /// <summary>Cloud sync and other background GetEntities floods.</summary>
        Sync
    }

    private static readonly SemaphoreSlim SyncGate = new(2, 2);

    private readonly Lane _lane;

    public GetEntitiesThrottlingHandler(Lane lane = Lane.Interactive)
    {
        _lane = lane;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_lane == Lane.Sync && IsGetEntities(request))
        {
            await SyncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                SyncGate.Release();
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    internal static bool IsGetEntities(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Post)
            return false;
        var path = request.RequestUri?.AbsolutePath ?? "";
        // Match both /api/Entities/GetEntities and accidental /api/api/Entities/GetEntities.
        return path.EndsWith("/Entities/GetEntities", StringComparison.OrdinalIgnoreCase);
    }
}
