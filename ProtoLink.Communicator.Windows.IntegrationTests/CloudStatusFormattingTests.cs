using System.Net;
using System.Net.Http;
using ProtoLink.Communicator.Windows.Services;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Offline: Cloud status strip must soft-fail HTTP 504/5xx openresty HTML
/// (WIN_CLOUD_STATUS_GATEWAY_HTML) — short Waiting for network, never raw HTML.
/// </summary>
[Trait("Category", "Offline")]
public class CloudStatusFormattingTests
{
    private const string OpenrestyHtml = """
        <html>
        <head><title>504 Gateway Time-out</title></head>
        <body>
        <center><h1>504 Gateway Time-out</h1></center>
        <hr><center>openresty</center>
        </body>
        </html>
        """;

    [Fact]
    public void LooksLikeHtmlBody_OpenrestyGatewayPage()
    {
        Assert.True(CloudStatusFormatting.LooksLikeHtmlBody(OpenrestyHtml));
        Assert.True(CloudStatusFormatting.LooksLikeHtmlBody("<!DOCTYPE html><html>openresty</html>"));
        Assert.False(CloudStatusFormatting.LooksLikeHtmlBody("{\"code\":\"file_too_large\"}"));
        Assert.False(CloudStatusFormatting.LooksLikeHtmlBody(null));
    }

    [Fact]
    public void FormatHttpFailureDetail_OmitsGatewayHtml()
    {
        var detail = CloudStatusFormatting.FormatHttpFailureDetail(
            HttpStatusCode.GatewayTimeout, OpenrestyHtml);
        Assert.Equal("(gateway response omitted)", detail);
        Assert.DoesNotContain("openresty", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<html", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatHttpFailureDetail_KeepsShortJsonBody()
    {
        var detail = CloudStatusFormatting.FormatHttpFailureDetail(
            HttpStatusCode.RequestEntityTooLarge, "{\"code\":\"file_too_large\"}");
        Assert.Contains("file_too_large", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatSyncFailureStatus_504Html_IsWaitingForNetwork()
    {
        var ex = new HttpRequestException(
            "HTTP 504 Gateway Time-out. Request: https://protolink.ru/api/Entities/GetEntities\nResponse: "
            + OpenrestyHtml,
            inner: null,
            statusCode: HttpStatusCode.GatewayTimeout);

        var status = CloudStatusFormatting.FormatSyncFailureStatus(ex);
        Assert.Equal(CloudStatusFormatting.WaitingForNetwork, status);
        Assert.DoesNotContain("<html", status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("openresty", status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("504", status, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatForceFailureStatus_502_IsWaitingForNetwork()
    {
        var ex = new HttpRequestException(
            "HTTP 502 Bad Gateway. Request: https://protolink.ru/api/Entities/GetEntities. Response: (gateway response omitted)",
            inner: null,
            statusCode: HttpStatusCode.BadGateway);

        Assert.Equal(
            CloudStatusFormatting.WaitingForNetwork,
            CloudStatusFormatting.FormatForceFailureStatus(ex, "download"));
    }

    [Fact]
    public void SanitizeMessage_StripsResponseHtmlDump()
    {
        var raw =
            "HTTP 504 Gateway Time-out. Request: https://protolink.ru/api/Entities/GetEntities\nResponse: "
            + OpenrestyHtml;
        var sanitized = CloudStatusFormatting.SanitizeMessage(raw);
        Assert.DoesNotContain("<html", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("openresty", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("504", sanitized, StringComparison.Ordinal);
        Assert.True(sanitized.Length < raw.Length);
    }

    [Fact]
    public void FormatSyncFailureStatus_NonHttp_KeepsShortErrorPrefix()
    {
        var status = CloudStatusFormatting.FormatSyncFailureStatus(
            new InvalidOperationException("mapping broke"));
        Assert.Equal("Error: mapping broke", status);
    }

    [Fact]
    public void ShouldUseWaitingForNetworkStatus_TransientGatewayCodes()
    {
        Assert.True(CloudStatusFormatting.ShouldUseWaitingForNetworkStatus(
            new HttpRequestException("gateway", null, HttpStatusCode.GatewayTimeout)));
        Assert.True(CloudStatusFormatting.ShouldUseWaitingForNetworkStatus(
            new HttpRequestException("gateway", null, HttpStatusCode.ServiceUnavailable)));
        Assert.True(CloudStatusFormatting.IsTransientGatewayStatus(HttpStatusCode.InternalServerError));
        Assert.False(CloudStatusFormatting.IsTransientGatewayStatus(HttpStatusCode.NotFound));
    }
}
