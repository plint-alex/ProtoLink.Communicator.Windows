using System.IO;

namespace ProtoLink.Communicator.Windows.Services.Notes;

/// <summary>
/// Watches a single index file: debounced <see cref="Changed"/> on writes; immediate <see cref="IndexFileDeleted"/> when the file is removed.
/// </summary>
public sealed class NotesIndexFileWatcher : IDisposable
{
    private readonly string _fullPath;
    private readonly FileSystemWatcher _watcher;
    private readonly System.Timers.Timer _debounce;
    private readonly object _gate = new();
    private bool _disposed;

    public NotesIndexFileWatcher(string indexFilePath)
    {
        _fullPath = Path.GetFullPath(indexFilePath);
        var dir = Path.GetDirectoryName(_fullPath);
        var name = Path.GetFileName(_fullPath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name))
            throw new ArgumentException("Invalid index file path.", nameof(indexFilePath));

        _debounce = new System.Timers.Timer(500) { AutoReset = false };
        _debounce.Elapsed += (_, _) => Changed?.Invoke();

        _watcher = new FileSystemWatcher(dir, name)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };
        _watcher.Changed += OnFsEvent;
        _watcher.Created += OnFsEvent;
        _watcher.Deleted += OnDeleted;
        _watcher.Renamed += OnRenamed;
        _watcher.EnableRaisingEvents = true;
    }

    public event Action? Changed;
    public event Action? IndexFileDeleted;

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _debounce.Stop();
            _debounce.Start();
        }
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        IndexFileDeleted?.Invoke();
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (!File.Exists(_fullPath))
            IndexFileDeleted?.Invoke();
        else
            OnFsEvent(sender, e);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _debounce.Stop();
            _debounce.Dispose();
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
    }
}
