using System.Net;
using System.Net.Http;
using ProtoLink.Communicator.Windows.Services;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class GetEntitiesThrottlingHandlerTests
{
    [Theory]
    [InlineData("https://protolink.ru/api/Entities/GetEntities", true)]
    [InlineData("https://protolink.ru/api/api/Entities/GetEntities", true)]
    [InlineData("https://protolink.ru/api/Version", false)]
    [InlineData("https://protolink.ru/api/Entities/AddEntity", false)]
    public void IsGetEntities_MatchesPathSuffix(string url, bool expected)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        Assert.Equal(expected, GetEntitiesThrottlingHandler.IsGetEntities(req));
    }

    [Fact]
    public void IsGetEntities_GetMethod_IsFalse()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://protolink.ru/api/Entities/GetEntities");
        Assert.False(GetEntitiesThrottlingHandler.IsGetEntities(req));
    }

    [Fact]
    public async Task InteractiveLane_DoesNotWaitOnSyncGate()
    {
        // Hold both Sync slots; Interactive GetEntities must still complete.
        var syncInner = new BlockingHandler();
        using var syncClient = new HttpClient(new GetEntitiesThrottlingHandler(GetEntitiesThrottlingHandler.Lane.Sync)
        {
            InnerHandler = syncInner
        });

        var sync1 = syncClient.PostAsync("https://example.test/api/Entities/GetEntities", new StringContent("{}"));
        var sync2 = syncClient.PostAsync("https://example.test/api/Entities/GetEntities", new StringContent("{}"));
        await syncInner.WaitUntilInFlightAsync(2);

        var interactiveInner = new ImmediateOkHandler();
        using var interactiveClient = new HttpClient(
            new GetEntitiesThrottlingHandler(GetEntitiesThrottlingHandler.Lane.Interactive)
            {
                InnerHandler = interactiveInner
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var response = await interactiveClient.PostAsync(
            "https://example.test/api/Entities/GetEntities",
            new StringContent("{}"),
            cts.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, interactiveInner.Calls);

        syncInner.ReleaseAll();
        await Task.WhenAll(sync1, sync2);
    }

    private sealed class ImmediateOkHandler : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        private int _inFlight;
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _reachedTwo = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WaitUntilInFlightAsync(int count)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Volatile.Read(ref _inFlight) < count)
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(10, cts.Token);
            }

            await _reachedTwo.Task.WaitAsync(cts.Token);
        }

        public void ReleaseAll() => _released.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _inFlight);
            if (n >= 2)
                _reachedTwo.TrySetResult();
            await _released.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
