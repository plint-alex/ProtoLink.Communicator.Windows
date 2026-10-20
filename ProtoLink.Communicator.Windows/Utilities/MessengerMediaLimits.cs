namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>Client pre-checks from messenger-media client-contract §7 / M11.</summary>
public static class MessengerMediaLimits
{
    /// <summary>100 MiB — matches server default <c>Files:MaxUploadBytes</c>.</summary>
    public const long MaxAttachmentBytes = 104_857_600;

    public const int MaxAttachmentsPerMessage = 10;

    public const string OversizeMessageEn = "Files up to 100 MB can be sent";
    public const string OfflineSendFailedEn = "Couldn't send";
    public const string UploadFailedEn = "Couldn't send";
    public const string CapRefuseEn = MessengerAttachmentRender.CapRefuseEn;

    /// <summary>Bubble thumbnail long side (logical px); multiply by device scale at request time.</summary>
    public const int BubbleImageLongSideLogical = 480;

    /// <summary>Lightbox preview long side (logical px).</summary>
    public const int LightboxImageLongSideLogical = 1600;

    public static bool IsOversize(long byteLength) => byteLength > MaxAttachmentBytes;

    public static string FormatAttachmentValue(Guid entityId) =>
        $"{MessageStringValues.AttachmentPrefix}{entityId:D}";
}
