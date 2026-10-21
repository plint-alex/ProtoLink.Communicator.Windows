using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using ProtoLink.Communicator.Windows.Services;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Expected connectivity faults soft-fail Messenger LoadMessages / App unobserved and MainWindow Sync error modal.
/// </summary>
[Trait("Category", "Offline")]
public class ApiExceptionHelperTests
{
    [Fact]
    public void IsExpectedConnectivityFailure_HttpRequestException_ConnectionRefused()
    {
        var ex = new HttpRequestException(
            "Connection refused (protolink.ru:443)",
            new SocketException((int)SocketError.ConnectionRefused));

        Assert.True(ApiExceptionHelper.IsExpectedConnectivityFailure(ex));
    }

    [Fact]
    public void IsExpectedConnectivityFailure_TaskCanceledException()
    {
        Assert.True(ApiExceptionHelper.IsExpectedConnectivityFailure(new TaskCanceledException("timeout")));
    }

    [Fact]
    public void IsExpectedConnectivityFailure_Aggregate_Of_SocketException()
    {
        var agg = new AggregateException(new SocketException((int)SocketError.TimedOut));
        Assert.True(ApiExceptionHelper.IsExpectedConnectivityFailure(agg));
    }

    [Fact]
    public void IsExpectedConnectivityFailure_IOException_WithoutPathNotFound()
    {
        Assert.True(ApiExceptionHelper.IsExpectedConnectivityFailure(new IOException("Unable to read data from the transport connection")));
    }

    [Fact]
    public void IsExpectedConnectivityFailure_False_For_Unrelated()
    {
        Assert.False(ApiExceptionHelper.IsExpectedConnectivityFailure(new InvalidOperationException("bug")));
        Assert.False(ApiExceptionHelper.IsExpectedConnectivityFailure(new FileNotFoundException("missing")));
        Assert.False(ApiExceptionHelper.IsExpectedConnectivityFailure(null));
    }

    [Fact]
    public void IndicatesUnauthorizedHttp_StillWorks_For_401()
    {
        var byStatus = new HttpRequestException("unauthorized", null, System.Net.HttpStatusCode.Unauthorized);
        var byMessage = new HttpRequestException("Response status code does not indicate success: 401 (Unauthorized).");
        Assert.True(ApiExceptionHelper.IndicatesUnauthorizedHttp(byStatus));
        Assert.True(ApiExceptionHelper.IndicatesUnauthorizedHttp(byMessage));
        // HttpRequestException soft-fails LoadMessages / App unobserved path (auth also SetObserved).
        Assert.True(ApiExceptionHelper.IsExpectedConnectivityFailure(byStatus));
    }
}
