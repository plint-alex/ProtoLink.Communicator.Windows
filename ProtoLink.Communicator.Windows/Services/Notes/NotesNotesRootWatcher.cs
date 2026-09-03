using System.IO;

namespace ProtoLink.Communicator.Windows.Services.Notes;

/// <summary>
/// Debounced notifications when anything under the notes root changes (folders/files).
/// </summary>
public sealed class NotesNotesRootWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly System.Timers.Timer _debounce;
    private readonly object _gate = new();
    private bool _disposed;

    public NotesNotesRootWatcher(string rootPath)
    {
        if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
            throw new ArgumentException("Root path must exist.", nameof(rootPath));

        _debounce = new System.Timers.Timer(400) { AutoReset = false };
        _debounce.Elapsed += (_, _) => TreeStructureChanged?.Invoke();

        _watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watcher.Created += OnFsEvent;
        _watcher.Deleted += OnFsEvent;
        _watcher.Changed += OnFsEvent;
        _watcher.Renamed += OnRenamed;
        _watcher.EnableRaisingEvents = true;
    }

    public event Action? TreeStructureChanged;

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _debounce.Stop();
            _debounce.Start();
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
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
