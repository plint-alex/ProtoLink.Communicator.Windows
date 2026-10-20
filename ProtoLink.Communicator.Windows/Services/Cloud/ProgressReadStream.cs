using System.IO;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// Read-through wrapper that reports cumulative bytes to <see cref="IProgress{T}"/>.
/// Used by <see cref="CloudApiService.AddFileAsync"/> so Cloud tab / sync can share the same progress path.
/// </summary>
public sealed class ProgressReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long? _totalBytes;
    private readonly IProgress<CloudUploadProgress>? _progress;
    private long _bytesRead;

    public ProgressReadStream(Stream inner, IProgress<CloudUploadProgress>? progress, long? totalBytes = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _progress = progress;
        _totalBytes = totalBytes
                      ?? (inner.CanSeek ? inner.Length : null);
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = _inner.Read(buffer, offset, count);
        Report(n);
        return n;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var n = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        Report(n);
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Report(n);
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    // Does not dispose _inner — caller owns the source stream (AddFileAsync / sync / Cloud upload).

    private void Report(int n)
    {
        if (n <= 0 || _progress == null) return;
        _bytesRead += n;
        _progress.Report(new CloudUploadProgress(_bytesRead, _totalBytes));
    }
}
