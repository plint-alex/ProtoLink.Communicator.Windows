using ProtoLink.Communicator.Windows.Services;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Live API client that never reports remote file size (listing + Content-Length probe).</summary>
internal sealed class NoRemoteSizeCloudApiService : CloudApiService
{
    public NoRemoteSizeCloudApiService(HttpClient http) : base(http) { }

    public override Task<long?> GetFileContentLengthOrNullAsync(
        Guid entityId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<long?>(null);
}
