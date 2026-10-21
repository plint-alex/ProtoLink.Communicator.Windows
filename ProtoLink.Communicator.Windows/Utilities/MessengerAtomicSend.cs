using System.Text.Json;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Pure helpers for <c>POST api/Messenger/sendWithAttachments</c> (client-contract §9).
/// Api creates new CloudFile ids server-side — retry of new uploads is a full re-send.
/// </summary>
public static class MessengerAtomicSend
{
    public const string EndpointPath = "api/Messenger/sendWithAttachments";

    public static string FormatExistingAttachmentIds(IReadOnlyList<Guid> ids)
    {
        if (ids == null || ids.Count == 0)
            return "[]";
        return JsonSerializer.Serialize(ids);
    }

    public static MessengerSendWithAttachmentsResult ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("messageId", out var msgEl) || !msgEl.TryGetGuid(out var messageId))
            throw new InvalidOperationException("sendWithAttachments response missing messageId.");

        var attachmentIds = new List<Guid>();
        if (root.TryGetProperty("attachmentIds", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                if (el.TryGetGuid(out var id))
                    attachmentIds.Add(id);
            }
        }

        return new MessengerSendWithAttachmentsResult
        {
            MessageId = messageId,
            AttachmentIds = attachmentIds
        };
    }

    public static bool LooksLikeFileTooLarge(string? httpMessage) =>
        !string.IsNullOrEmpty(httpMessage)
        && (httpMessage.Contains("file_too_large", StringComparison.OrdinalIgnoreCase)
            || httpMessage.Contains("HTTP 413", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Cloud picks keep their entity ids; new uploads clear local ids so retry posts bytes again.
    /// </summary>
    public static void PrepareDraftsForAtomicRetry(IList<PendingAttachmentDraft> drafts)
    {
        foreach (var d in drafts)
        {
            if (d.IsCloudPick)
            {
                d.CloudFileEntityId = MessengerSendStateMachine.ResolveEntityIdForRetry(d.CloudFileEntityId, null);
                d.UploadCompleted = d.CloudFileEntityId != Guid.Empty;
            }
            else
            {
                d.CloudFileEntityId = Guid.Empty;
                d.UploadCompleted = false;
            }
        }
    }

    public static IReadOnlyList<Guid> CollectExistingIds(IEnumerable<PendingAttachmentDraft> drafts) =>
        drafts
            .Where(d => d.IsCloudPick && d.CloudFileEntityId != Guid.Empty)
            .Select(d => d.CloudFileEntityId)
            .ToList();

    public static IReadOnlyList<PendingAttachmentDraft> CollectNewUploads(IEnumerable<PendingAttachmentDraft> drafts) =>
        drafts.Where(d => !d.IsCloudPick).ToList();
}

public sealed class MessengerSendWithAttachmentsResult
{
    public Guid MessageId { get; init; }

    public IReadOnlyList<Guid> AttachmentIds { get; init; } = Array.Empty<Guid>();
}
