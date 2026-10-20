using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class MessageStringValuesParseTests
{
    private static readonly Guid AttachmentId = Guid.Parse("3f2b1c44-9a0e-4f2b-8b1e-7a0c5d2e9f10");
    private static readonly Guid AttachmentId2 = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void PrefixParsing_YieldsBodyAndOneAttachmentId()
    {
        var values = new[]
        {
            "sender:11111111-1111-1111-1111-111111111111",
            "receiver:22222222-2222-2222-2222-222222222222",
            "status:read",
            $"attachment:{AttachmentId:D}",
            "hello body"
        };

        var (body, ids) = MessageStringValues.Parse(values);

        Assert.Equal("hello body", body);
        Assert.Equal(new[] { AttachmentId }, ids);
    }

    [Fact]
    public void SeveralAttachments_PreservesOrder()
    {
        var values = new[]
        {
            "caption",
            $"attachment:{AttachmentId:D}",
            $"attachment:{AttachmentId2:D}"
        };

        var (body, ids) = MessageStringValues.Parse(values);

        Assert.Equal("caption", body);
        Assert.Equal(new[] { AttachmentId, AttachmentId2 }, ids);
    }

    [Fact]
    public void MalformedAttachment_IsSkippedNotRenderedAsText()
    {
        var values = new[]
        {
            "sender:11111111-1111-1111-1111-111111111111",
            "attachment:not-a-guid",
            "attachment:",
            "real text",
            $"attachment:{AttachmentId:D}"
        };

        var (body, ids) = MessageStringValues.Parse(values);

        Assert.Equal("real text", body);
        Assert.DoesNotContain("not-a-guid", body);
        Assert.DoesNotContain("attachment:", body);
        Assert.Equal(new[] { AttachmentId }, ids);
    }

    [Fact]
    public void AttachmentOnly_YieldsEmptyBodyAndIds()
    {
        var values = new[]
        {
            $"attachment:{AttachmentId:D}"
        };

        var (body, ids) = MessageStringValues.Parse(values);

        Assert.Equal(string.Empty, body);
        Assert.Equal(new[] { AttachmentId }, ids);
    }
}
