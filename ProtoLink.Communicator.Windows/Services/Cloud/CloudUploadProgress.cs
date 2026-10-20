namespace ProtoLink.Communicator.Windows.Services;

/// <summary>Bytes transferred while uploading a Cloud file body (multipart <c>addFile</c>).</summary>
public readonly record struct CloudUploadProgress(long BytesSent, long? TotalBytes)
{
    public double? Fraction =>
        TotalBytes is > 0 ? Math.Clamp(BytesSent / (double)TotalBytes.Value, 0d, 1d) : null;
}
