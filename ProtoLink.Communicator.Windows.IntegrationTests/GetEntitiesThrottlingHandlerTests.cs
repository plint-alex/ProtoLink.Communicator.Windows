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
        // Hold all Sync slots; Interactive GetEntities must still complete.
        var syncInner = new BlockingHandler();
        using var syncClient = new HttpClient(new GetEntitiesThrottlingHandler(GetEntitiesThrottlingHandler.Lane.Sync)
        {
            InnerHandler = syncInner
        });

        var n = GetEntitiesThrottlingHandler.SyncLaneConcurrency;
        var held = Enumerable.Range(0, n)
            .Select(_ => syncClient.PostAsync("https://example.test/api/Entities/GetEntities", new StringContent("{}")))
            .ToArray();
        await syncInner.WaitUntilInFlightAsync(n);

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
        await Task.WhenAll(held);
    }

    /// <summary>
    /// Photos / Cloud browse uses Interactive lane while mapped sync holds Sync slots —
    /// listing must still complete (false-empty chrome regression guard).
    /// </summary>
    [Fact]
    public async Task SyncLane_OverflowGetEntities_WaitsUntilSlotFrees()
    {
        var syncInner = new BlockingHandler();
        using var syncClient = new HttpClient(new GetEntitiesThrottlingHandler(GetEntitiesThrottlingHandler.Lane.Sync)
        {
            InnerHandler = syncInner
        });

        var n = GetEntitiesThrottlingHandler.SyncLaneConcurrency;
        var held = Enumerable.Range(0, n)
            .Select(_ => syncClient.PostAsync("https://example.test/api/Entities/GetEntities", new StringContent("{}")))
            .ToArray();
        await syncInner.WaitUntilInFlightAsync(n);

        var overflowStarted = false;
        var overflow = Task.Run(async () =>
        {
            overflowStarted = true;
            return await syncClient.PostAsync(
                "https://example.test/api/Entities/GetEntities",
                new StringContent("{}"));
        });

        await Task.Delay(150);
        Assert.True(overflowStarted);
        Assert.False(overflow.IsCompleted);

        syncInner.ReleaseAll();
        using var rOverflow = await overflow.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(HttpStatusCode.OK, rOverflow.StatusCode);
        await Task.WhenAll(held);
    }

    [Fact]
    public async Task SyncLane_CancelDuringSend_ReleasesGateForNextCaller()
    {
        var syncInner = new BlockingHandler();
        using var syncClient = new HttpClient(new GetEntitiesThrottlingHandler(GetEntitiesThrottlingHandler.Lane.Sync)
        {
            InnerHandler = syncInner
        });

        using var cts = new CancellationTokenSource();
        var first = syncClient.PostAsync(
            "https://example.test/api/Entities/GetEntities",
            new StringContent("{}"),
            cts.Token);
        await syncInner.WaitUntilInFlightAsync(1);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first);

        // Gate must be free — second call completes without waiting on a leaked slot.
        syncInner.ReleaseAll();
        using var second = await syncClient.PostAsync(
            "https://example.test/api/Entities/GetEntities",
            new StringContent("{}")).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
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

        public async Task WaitUntilInFlightAsync(int count)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Volatile.Read(ref _inFlight) < count)
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(10, cts.Token);
            }
        }

        public void ReleaseAll() => _released.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _inFlight);
            await _released.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
