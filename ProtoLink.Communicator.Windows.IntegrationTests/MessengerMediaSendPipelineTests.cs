using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class MessengerMediaSendPipelineTests
{
    [Fact]
    public void SendStateMachine_Uploading_Sending_Sent()
    {
        var status = MessengerSendStateMachine.OnBubbleCreated(hasAttachments: true);
        Assert.Equal(MessageDeliveryStatus.Uploading, status);

        status = MessengerSendStateMachine.OnUploadFinished();
        Assert.Equal(MessageDeliveryStatus.Sending, status);

        status = MessengerSendStateMachine.OnMessageCommitted();
        Assert.Equal(MessageDeliveryStatus.Sent, status);
    }

    [Fact]
    public void SendStateMachine_Failed_Retry_ReusesEntityId()
    {
        var entityId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var status = MessengerSendStateMachine.OnBubbleCreated(hasAttachments: true);
        Assert.Equal(MessageDeliveryStatus.Uploading, status);

        status = MessengerSendStateMachine.OnFailure();
        Assert.Equal(MessageDeliveryStatus.Failed, status);

        status = MessengerSendStateMachine.OnRetry();
        Assert.Equal(MessageDeliveryStatus.Uploading, status);

        var reused = MessengerSendStateMachine.ResolveEntityIdForRetry(entityId, newlyCreatedId: Guid.NewGuid());
        Assert.Equal(entityId, reused);
    }

    [Fact]
    public void ChatFolderNaming_Collision_ProducesTwoDistinctFolders()
    {
        var idA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var idB = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var (first, second) = ChatFolderNaming.DistinctNamesForCollision("Ivan Petrov", idA, idB);

        Assert.Equal("Ivan Petrov", first);
        Assert.Equal($"Ivan Petrov ({ChatFolderNaming.ShortId(idB)})", second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void SizePreCheck_RefusesOver100Mb()
    {
        Assert.True(MessengerMediaLimits.IsOversize(MessengerMediaLimits.MaxAttachmentBytes + 1));
        Assert.False(MessengerMediaLimits.IsOversize(MessengerMediaLimits.MaxAttachmentBytes));
        Assert.Equal("Files up to 100 MB can be sent", MessengerMediaLimits.OversizeMessageEn);
    }

    [Fact]
    public void AttachmentValue_Format_MatchesContract()
    {
        var id = Guid.Parse("3f2b1c44-9a0e-4f2b-8b1e-7a0c5d2e9f10");
        Assert.Equal($"attachment:{id:D}", MessengerMediaLimits.FormatAttachmentValue(id));
    }

    [Fact]
    public void AtomicSend_FormatExistingIds_JsonArray()
    {
        var a = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var b = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Assert.Equal("[]", MessengerAtomicSend.FormatExistingAttachmentIds(Array.Empty<Guid>()));
        var json = MessengerAtomicSend.FormatExistingAttachmentIds(new[] { a, b });
        Assert.Contains(a.ToString("D"), json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(b.ToString("D"), json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AtomicSend_ParseResponse_MessageAndAttachments()
    {
        var messageId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var att = Guid.Parse("3f2b1c44-9a0e-4f2b-8b1e-7a0c5d2e9f10");
        var result = MessengerAtomicSend.ParseResponse(
            $"{{\"messageId\":\"{messageId:D}\",\"attachmentIds\":[\"{att:D}\"]}}");
        Assert.Equal(messageId, result.MessageId);
        Assert.Equal(new[] { att }, result.AttachmentIds);
    }

    [Fact]
    public void AtomicSend_PrepareRetry_ClearsNewUploadIds_KeepsCloudPick()
    {
        var pickId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var drafts = new List<PendingAttachmentDraft>
        {
            new()
            {
                LocalPath = @"C:\tmp\a.png",
                FileName = "a.png",
                CloudFileEntityId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                UploadCompleted = false,
                IsCloudPick = false
            },
            new()
            {
                LocalPath = string.Empty,
                FileName = "cloud.bin",
                CloudFileEntityId = pickId,
                UploadCompleted = true,
                IsCloudPick = true
            }
        };

        MessengerAtomicSend.PrepareDraftsForAtomicRetry(drafts);

        Assert.Equal(Guid.Empty, drafts[0].CloudFileEntityId);
        Assert.False(drafts[0].UploadCompleted);
        Assert.Equal(pickId, drafts[1].CloudFileEntityId);
        Assert.True(drafts[1].UploadCompleted);
    }

    [Fact]
    public void AtomicSend_LooksLikeFileTooLarge()
    {
        Assert.True(MessengerAtomicSend.LooksLikeFileTooLarge("HTTP 413 Payload Too Large. Response: {\"code\":\"file_too_large\"}"));
        Assert.False(MessengerAtomicSend.LooksLikeFileTooLarge("HTTP 500"));
    }

    [Fact]
    public void ProgressReadStream_ReportsBytes()
    {
        // Use a sync IProgress — System.Progress<T> posts via SynchronizationContext and
        // can leave the handler unrun before assertions in unit tests.
        var reports = new List<CloudUploadProgress>();
        IProgress<CloudUploadProgress> progress = new SyncProgress<CloudUploadProgress>(reports.Add);
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        using var ms = new MemoryStream(payload);
        using var wrapped = new ProgressReadStream(ms, progress, payload.Length);

        var buffer = new byte[3];
        var total = 0;
        int n;
        while ((n = wrapped.Read(buffer, 0, buffer.Length)) > 0)
            total += n;

        Assert.Equal(payload.Length, total);
        Assert.NotEmpty(reports);
        Assert.Equal(payload.Length, reports[^1].BytesSent);
        Assert.Equal(payload.Length, reports[^1].TotalBytes);
        Assert.Equal(1d, reports[^1].Fraction);
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
