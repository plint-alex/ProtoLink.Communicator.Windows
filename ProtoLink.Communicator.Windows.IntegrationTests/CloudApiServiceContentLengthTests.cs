using System.Net;
using System.Net.Http;
using System.Text;
using ProtoLink.Communicator.Windows.Services;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

public class CloudApiServiceContentLengthTests
{
    private sealed class ChunkedNoLengthHandler : HttpMessageHandler
    {
        private readonly byte[] _body;

        public ChunkedNoLengthHandler(byte[] body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_body)
            };
            // Simulate OpenResty/ASP.NET chunked getFile: no Content-Length header.
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");
            response.Content.Headers.ContentLength = null;
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task GetFileContentLength_WhenChunkedWithoutHeader_ReturnsNull_DoesNotRequireBody()
    {
        var body = Encoding.UTF8.GetBytes("bbbb");
        var http = new HttpClient(new ChunkedNoLengthHandler(body))
        {
            BaseAddress = new Uri("https://example.test/")
        };
        var api = new CloudApiService(http);

        var len = await api.GetFileContentLengthOrNullAsync(Guid.NewGuid());

        // Must not download/measure body — that stalled reconcile over large mapped trees.
        Assert.Null(len);
    }

    [Fact]
    public async Task GetFileContentLength_WhenHeaderPresent_ReturnsLength()
    {
        var body = Encoding.UTF8.GetBytes("bbbb");
        var http = new HttpClient(new FixedLengthHandler(body))
        {
            BaseAddress = new Uri("https://example.test/")
        };
        var api = new CloudApiService(http);

        var len = await api.GetFileContentLengthOrNullAsync(Guid.NewGuid());

        Assert.Equal(4L, len);
    }

    private sealed class FixedLengthHandler : HttpMessageHandler
    {
        private readonly byte[] _body;
        public FixedLengthHandler(byte[] body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_body)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");
            response.Content.Headers.ContentLength = _body.LongLength;
            return Task.FromResult(response);
        }
    }
}
