namespace ProtoLink.Communicator.Windows.Models;

/// <summary>Result of getFile / getImage with 403/404 discrimination (entity-read-error-contract).</summary>
public sealed class CloudFileBytesResult
{
    public enum Kind
    {
        Success,
        NotFound,
        NoAccess,
        Failed
    }

    public Kind Status { get; private init; }
    public byte[]? Bytes { get; private init; }
    public string? ContentType { get; private init; }
    public int? HttpStatus { get; private init; }
    public string? ErrorBody { get; private init; }

    public static CloudFileBytesResult Ok(byte[] bytes, string? contentType) => new()
    {
        Status = Kind.Success,
        Bytes = bytes,
        ContentType = contentType,
        HttpStatus = 200
    };

    public static CloudFileBytesResult NotFound() => new()
    {
        Status = Kind.NotFound,
        HttpStatus = 404
    };

    public static CloudFileBytesResult NoAccess() => new()
    {
        Status = Kind.NoAccess,
        HttpStatus = 403
    };

    public static CloudFileBytesResult Failed(int httpStatus, string? body) => new()
    {
        Status = Kind.Failed,
        HttpStatus = httpStatus,
        ErrorBody = body
    };
}
