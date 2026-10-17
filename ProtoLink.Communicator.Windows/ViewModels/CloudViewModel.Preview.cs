using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class CloudViewModel
{
    private const int MaxPreviewTextBytes = 1_048_576;
    private CancellationTokenSource? _previewCts;
    private string? _previewTempPath;
    private CloudFilePreviewKind _previewKind = CloudFilePreviewKind.Other;
    private bool _isPreviewLoading;
    private string? _previewError;
    private BitmapImage? _previewImage;
    private string? _previewText;
    private string? _previewOpenPath;
    private string _previewTypeGlyph = "📄";

    public CloudFilePreviewKind PreviewKind
    {
        get => _previewKind;
        private set
        {
            if (_previewKind == value) return;
            _previewKind = value;
            OnPropertyChanged();
            NotifyPreviewVisibility();
        }
    }

    public bool IsPreviewLoading
    {
        get => _isPreviewLoading;
        private set
        {
            if (_isPreviewLoading == value) return;
            _isPreviewLoading = value;
            OnPropertyChanged();
            NotifyPreviewVisibility();
        }
    }

    public string? PreviewError
    {
        get => _previewError;
        private set
        {
            if (_previewError == value) return;
            _previewError = value;
            OnPropertyChanged();
            NotifyPreviewVisibility();
        }
    }

    public BitmapImage? PreviewImage
    {
        get => _previewImage;
        private set { _previewImage = value; OnPropertyChanged(); }
    }

    public string? PreviewText
    {
        get => _previewText;
        private set { _previewText = value; OnPropertyChanged(); }
    }

    /// <summary>Local or temp path for PDF WebView2 / shell Open.</summary>
    public string? PreviewOpenPath
    {
        get => _previewOpenPath;
        private set
        {
            if (_previewOpenPath == value) return;
            _previewOpenPath = value;
            OnPropertyChanged();
        }
    }

    public string PreviewTypeGlyph
    {
        get => _previewTypeGlyph;
        private set
        {
            if (_previewTypeGlyph == value) return;
            _previewTypeGlyph = value;
            OnPropertyChanged();
        }
    }

    public bool ShowPreviewLoading => IsFileOpen && IsPreviewLoading;
    public bool ShowPreviewError => IsFileOpen && !IsPreviewLoading && !string.IsNullOrEmpty(PreviewError);
    public bool ShowPreviewImage => IsFileOpen && !IsPreviewLoading && PreviewError is null && PreviewKind == CloudFilePreviewKind.Image;
    public bool ShowPreviewText => IsFileOpen && !IsPreviewLoading && PreviewError is null && PreviewKind == CloudFilePreviewKind.Text;
    public bool ShowPreviewPdf => IsFileOpen && !IsPreviewLoading && PreviewError is null && PreviewKind == CloudFilePreviewKind.Pdf;
    public bool ShowPreviewOther => IsFileOpen && !IsPreviewLoading && PreviewError is null && PreviewKind == CloudFilePreviewKind.Other;

    /// <summary>Shell-open local mirror or a temp copy of the open file.</summary>
    public ICommand OpenExternallyCommand { get; private set; } = null!;

    private void InitPreviewCommands()
    {
        OpenExternallyCommand = new RelayCommand(
            _ => OpenPreviewExternally(),
            _ => IsFileOpen && (!string.IsNullOrEmpty(PreviewOpenPath) || !IsPreviewLoading));
    }

    private void CancelPreviewLoad()
    {
        try { _previewCts?.Cancel(); } catch { /* ignore */ }
        _previewCts?.Dispose();
        _previewCts = null;
    }

    private void ClearPreviewUi()
    {
        PreviewKind = CloudFilePreviewKind.Other;
        IsPreviewLoading = false;
        PreviewError = null;
        PreviewImage = null;
        PreviewText = null;
        PreviewOpenPath = null;
        PreviewTypeGlyph = "📄";
        DeletePreviewTemp();
    }

    private void DeletePreviewTemp()
    {
        if (string.IsNullOrEmpty(_previewTempPath)) return;
        try
        {
            if (File.Exists(_previewTempPath))
                File.Delete(_previewTempPath);
        }
        catch
        {
            // temp best-effort
        }
        _previewTempPath = null;
    }

    private void NotifyPreviewVisibility()
    {
        OnPropertyChanged(nameof(ShowPreviewLoading));
        OnPropertyChanged(nameof(ShowPreviewError));
        OnPropertyChanged(nameof(ShowPreviewImage));
        OnPropertyChanged(nameof(ShowPreviewText));
        OnPropertyChanged(nameof(ShowPreviewPdf));
        OnPropertyChanged(nameof(ShowPreviewOther));
        CommandManager.InvalidateRequerySuggested();
    }

    private void BeginFilePreview()
    {
        CancelPreviewLoad();
        ClearPreviewUi();
        if (CurrentEntity is not { IsFolder: false } file) return;

        PreviewKind = CloudFilePreview.KindFromFileName(file.Name);
        PreviewTypeGlyph = PreviewKind switch
        {
            CloudFilePreviewKind.Image => "🖼",
            CloudFilePreviewKind.Text => "📝",
            CloudFilePreviewKind.Pdf => "📕",
            _ => GlyphForExtension(file.Name)
        };

        var cts = new CancellationTokenSource();
        _previewCts = cts;
        _ = LoadFilePreviewAsync(file, cts.Token);
    }

    private async Task LoadFilePreviewAsync(CloudEntityRef file, CancellationToken cancellationToken)
    {
        IsPreviewLoading = true;
        PreviewError = null;
        try
        {
            var mirror = CloudFilePreview.TryGetExistingMirrorPath(file.Name, BreadcrumbPath.ToList(), SyncMappings.ToList());
            if (CloudFilePreview.PreferLocalMirror(mirror))
                await LoadPreviewFromLocalAsync(file, mirror!, cancellationToken);
            else
                await LoadPreviewFromRemoteAsync(file, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // superseded by another open
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                PreviewError = ex.Message;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsPreviewLoading = false;
                NotifyPreviewVisibility();
            }
        }
    }

    private async Task LoadPreviewFromLocalAsync(CloudEntityRef file, string path, CancellationToken cancellationToken)
    {
        PreviewOpenPath = path;
        switch (PreviewKind)
        {
            case CloudFilePreviewKind.Image:
                PreviewImage = await Task.Run(() => LoadBitmapFromPath(path), cancellationToken);
                break;
            case CloudFilePreviewKind.Text:
                PreviewText = await ReadTextPreviewAsync(path, cancellationToken);
                break;
            case CloudFilePreviewKind.Pdf:
            case CloudFilePreviewKind.Other:
                break;
        }
    }

    private async Task LoadPreviewFromRemoteAsync(CloudEntityRef file, CancellationToken cancellationToken)
    {
        await using var stream = await _api.GetFileStreamOrNotFoundAsync(file.Id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (stream is null)
        {
            PreviewError = "File not found";
            return;
        }

        switch (PreviewKind)
        {
            case CloudFilePreviewKind.Image:
                PreviewImage = await LoadBitmapFromStreamAsync(stream, cancellationToken);
                break;
            case CloudFilePreviewKind.Text:
                PreviewText = await ReadTextPreviewFromStreamAsync(stream, cancellationToken);
                break;
            case CloudFilePreviewKind.Pdf:
            case CloudFilePreviewKind.Other:
                PreviewOpenPath = await MaterializeTempCopyAsync(file.Name, stream, cancellationToken);
                break;
        }
    }

    private async Task<string> MaterializeTempCopyAsync(string fileName, Stream stream, CancellationToken cancellationToken)
    {
        DeletePreviewTemp();
        var dir = Path.Combine(Path.GetTempPath(), "ProtoLinkCloudPreview");
        Directory.CreateDirectory(dir);
        var safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "file";
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + "_" + safeName);
        await using (var file = File.Create(path))
            await stream.CopyToAsync(file, cancellationToken);
        _previewTempPath = path;
        return path;
    }

    private static BitmapImage LoadBitmapFromPath(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static async Task<BitmapImage> LoadBitmapFromStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static async Task<string> ReadTextPreviewAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await ReadTextPreviewFromStreamAsync(stream, cancellationToken);
    }

    private static async Task<string> ReadTextPreviewFromStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var limited = new LimitedReadStream(stream, MaxPreviewTextBytes + 1);
        using var reader = new StreamReader(limited);
        var text = await reader.ReadToEndAsync(cancellationToken);
        if (text.Length > MaxPreviewTextBytes)
            return text[..MaxPreviewTextBytes] + "\n\n… (truncated)";
        return text;
    }

    private async void OpenPreviewExternally()
    {
        if (CurrentEntity is not { IsFolder: false } file) return;
        try
        {
            var path = PreviewOpenPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                var mirror = CloudFilePreview.TryGetExistingMirrorPath(file.Name, BreadcrumbPath.ToList(), SyncMappings.ToList());
                if (CloudFilePreview.PreferLocalMirror(mirror))
                    path = mirror;
                else
                {
                    StatusText = "Preparing file…";
                    await using var stream = await _api.GetFileStreamAsync(file.Id);
                    path = await MaterializeTempCopyAsync(file.Name, stream, CancellationToken.None);
                    PreviewOpenPath = path;
                }
            }

            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            StatusText = "Opened";
        }
        catch (Exception ex)
        {
            ReportCaughtException(ex);
        }
    }

    private static string GlyphForExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".zip" or ".7z" or ".rar" => "🗜",
            ".mp3" or ".wav" or ".flac" => "🎵",
            ".mp4" or ".mkv" or ".avi" => "🎬",
            ".doc" or ".docx" => "📘",
            ".xls" or ".xlsx" => "📗",
            _ => "📄"
        };
    }

    /// <summary>Caps unread bytes so huge remote text previews do not allocate unboundedly.</summary>
    private sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _limit;
        private long _read;

        public LimitedReadStream(Stream inner, long limit)
        {
            _inner = inner;
            _limit = limit;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read >= _limit) return 0;
            var allowed = (int)Math.Min(count, _limit - _read);
            var n = _inner.Read(buffer, offset, allowed);
            _read += n;
            return n;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_read >= _limit) return 0;
            var allowed = (int)Math.Min(count, _limit - _read);
            var n = await _inner.ReadAsync(buffer.AsMemory(offset, allowed), cancellationToken);
            _read += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
