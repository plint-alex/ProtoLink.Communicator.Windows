using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using ProtoLink.Communicator.Windows.Dialogs;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Notes;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class NotesViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly INoteRemoteIdResolver? _remoteIdResolver;
    private readonly ISyncMappingStore? _syncStore;
    private readonly NotesFileService _fileService;
    private readonly NotesFileSystemService _fsService;
    private readonly EditableMarkdownRenderer _renderer;
    private readonly NotesPageTitleRenameService _titleRename;
    private string? _rootPath;
    private TreeItemViewModel? _treeRoot;
    private TreeItemViewModel? _siblingTreeRoot;
    private bool _showSiblingFiles;
    private string? _currentPagePath;
    private string _currentHtml = string.Empty;
    private string _htmlSyncedToDisk = string.Empty;
    private string _statusText = "Ready";
    private CancellationTokenSource? _saveCts;
    private bool _pendingDebouncedSave;

    public NotesViewModel(
        ISettingsService settingsService,
        INoteRemoteIdResolver? remoteIdResolver = null,
        ISyncMappingStore? syncStore = null,
        CloudApiService? shareApi = null,
        Func<Guid?>? shareCurrentUserId = null,
        Func<bool>? shareIsOnline = null,
        Action<string>? onSendShareLinkInMessenger = null)
    {
        _settingsService = settingsService;
        _remoteIdResolver = remoteIdResolver;
        _syncStore = syncStore;
        _fileService = new NotesFileService();
        _fsService = new NotesFileSystemService();
        _renderer = new EditableMarkdownRenderer();
        _titleRename = new NotesPageTitleRenameService(_fileService, _fsService);
        _rootPath = _settingsService.LoadSettings().NotesRootPath;
        NewFolderCommand = new RelayCommand(_ => RaiseRequestNewFolder(), _ => !NeedsNotesRoot);
        CreateFirstNoteCommand = new RelayCommand(_ => RaiseRequestNewFolder(), _ => IsNotesEmpty);
        ChooseNotesRootCommand = new RelayCommand(_ => RequestChooseNotesRoot?.Invoke());
        AddNotesRootToCloudCommand = new RelayCommand(
            _ => RequestAddNotesRootToCloud?.Invoke(_rootPath!),
            _ => ShowMapToCloudNudge);
        RefreshCommand = new RelayCommand(_ => RefreshTree());
        OpenSiblingFileCommand = new RelayCommand(
            p => OpenSiblingPath(p as string),
            p => p is string path && File.Exists(path));
        InitNavigationCommands();
        InitShare(shareApi, shareCurrentUserId, shareIsOnline, onSendShareLinkInMessenger);
        RefreshTree();
        RefreshMapToCloudNudge();
    }

    public string? RootPath { get => _rootPath; private set { _rootPath = value; OnPropertyChanged(); } }
    public TreeItemViewModel? TreeRoot
    {
        get => _treeRoot;
        private set
        {
            _treeRoot = value;
            OnPropertyChanged();
            NotifyNotesListStateChanged();
        }
    }

    /// <summary>Notes root unset or folder missing — must not look like “no notes yet”.</summary>
    public bool NeedsNotesRoot =>
        string.IsNullOrWhiteSpace(_rootPath) || !Directory.Exists(_rootPath);

    /// <summary>Root is configured but has no child note folders yet.</summary>
    public bool IsNotesEmpty =>
        !NeedsNotesRoot
        && _treeRoot != null
        && _treeRoot.Children.Count == 0;

    /// <summary>Show the Notes tree (not setup / empty states).</summary>
    public bool ShowNotesTree => !NeedsNotesRoot && !IsNotesEmpty;

    /// <summary>E6: Notes root set but not covered by any Cloud mapping LocalPath.</summary>
    public bool ShowMapToCloudNudge { get; private set; }

    public string MapToCloudNudgeText =>
        MapLocalFolderStrings.NotesNudgeText(_settingsService.LoadSettings().UiLanguage);

    public string? CurrentPagePath
    {
        get => _currentPagePath;
        set
        {
            _currentPagePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPageTitle));
            OnPropertyChanged(nameof(HasOpenPage));
            OnPropertyChanged(nameof(HeaderTitle));
            OnPropertyChanged(nameof(HeaderSubtitle));
            OnPropertyChanged(nameof(CanNavigateParent));
            if (string.IsNullOrEmpty(value))
            {
                CurrentRemoteId = null;
                // Clear Folder-files panel immediately; do not enumerate FS on the UI thread here
                // (#notes-responsiveness — callers RefreshSiblingFiles after chrome paint).
                SiblingTreeRoot = null;
            }
        }
    }

    /// <summary>Local FS children under the open note folder (E5); null when empty / no page.</summary>
    public TreeItemViewModel? SiblingTreeRoot
    {
        get => _siblingTreeRoot;
        private set
        {
            _siblingTreeRoot = value;
            OnPropertyChanged();
            SyncSiblingFilesCollection();
            _showSiblingFiles = value != null && value.Children.Count > 0;
            // Always raise so NotesView re-applies row Height/Visibility after navigate/refresh.
            OnPropertyChanged(nameof(ShowSiblingFiles));
        }
    }

    /// <summary>Flat ItemsSource for Folder files TreeView (avoids fragile SiblingTreeRoot.Children path).</summary>
    public ObservableCollection<TreeItemViewModel> SiblingFiles { get; } = new();

    public bool ShowSiblingFiles => _showSiblingFiles;
    public string CurrentPageTitle =>
        string.IsNullOrEmpty(_currentPagePath) ? "Select a note" : Path.GetFileName(_currentPagePath);
    public bool HasOpenPage => !string.IsNullOrEmpty(_currentPagePath);
    public string? UiLanguage => _settingsService.LoadSettings().UiLanguage;
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public ICommand NewFolderCommand { get; }
    public ICommand CreateFirstNoteCommand { get; }
    public ICommand ChooseNotesRootCommand { get; }
    public ICommand AddNotesRootToCloudCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenSiblingFileCommand { get; }
    public event Action? TreeRefreshed;
    public event Action? RequestNewFolder;
    public event Action? RequestChooseNotesRoot;
    public event Action<string>? RequestAddNotesRootToCloud;
    public event Action<string>? ContentSaved;
    public event Action? InternalNoteWriteStarting;
    public event Action? InternalNoteWriteCompleted;
    public event Action<string>? NotesPagePathChanged;
    public event Action? NotesRootPathChanged;

    public bool IsDirty => !string.Equals(_currentHtml, _htmlSyncedToDisk, StringComparison.Ordinal);
    public bool HasPendingDebouncedSave => _pendingDebouncedSave;
    public bool HasUnsavedWork => IsDirty || HasPendingDebouncedSave;
    /// <summary>Last HTML body known to match disk (for skip-reload when unchanged).</summary>
    public string HtmlSyncedToDisk => _htmlSyncedToDisk;

    public string GetWatchedIndexPath(string folderPath) => _fsService.GetWriteIndexFilePath(folderPath);

    public async Task<string> ReadDiskInnerHtmlAsync(string folderPath)
    {
        if (!_fsService.HasIndexFile(folderPath))
            return "<p><br></p>";
        var indexPath = _fsService.GetIndexFilePath(folderPath);
        return await _fileService.OpenFileAsync(indexPath);
    }

    private static void PostUi(Action action)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d == null)
        {
            action();
            return;
        }

        if (d.CheckAccess())
            action();
        else
            d.BeginInvoke(action);
    }
    public void CancelPendingSave()
    {
        _saveCts?.Cancel();
        _saveCts = null;
        _pendingDebouncedSave = false;
    }

    public async Task RestoreDeletedPageFromUnsavedAsync()
    {
        if (string.IsNullOrEmpty(CurrentPagePath))
            return;
        var folderPath = CurrentPagePath;
        var html = _currentHtml;
        Directory.CreateDirectory(folderPath);
        var indexPath = _fsService.GetWriteIndexFilePath(folderPath);
        await _fileService.SaveFileAsync(indexPath, html);
        _htmlSyncedToDisk = html;
        CancelPendingSave();
        RefreshTree();
        StatusText = "Restored page from unsaved content";
    }

    public void CreateFolder(string parentPath, string folderName)
    {
        _fsService.CreateFolder(parentPath, folderName);
        var newPath = Path.Combine(parentPath, folderName);
        var indexPath = Path.Combine(newPath, "index.html");
        File.WriteAllText(indexPath, $"<h1>{System.Net.WebUtility.HtmlEncode(folderName)}</h1>\n\n<p><br></p>", Encoding.UTF8);
        CurrentPagePath = newPath;
        RefreshTree();
        NotesPagePathChanged?.Invoke(newPath);
    }

    public void ReloadFromSettings()
    {
        _rootPath = _settingsService.LoadSettings().NotesRootPath;
        OnPropertyChanged(nameof(RootPath));
        NotifyNotesListStateChanged();
        RefreshTree();
        RefreshMapToCloudNudge();
        NotesRootPathChanged?.Invoke();
    }

    /// <summary>Recompute E6 Notes→Cloud nudge after mappings change (or Notes root change).</summary>
    public void RefreshMapToCloudNudge()
    {
        var covered = false;
        if (!NeedsNotesRoot && _syncStore != null)
        {
            var paths = _syncStore.Load().Select(m => m.LocalPath);
            covered = LocalPathCoverage.IsCoveredByAnyMappingRoot(_rootPath, paths);
        }

        var show = !NeedsNotesRoot && !covered;
        if (ShowMapToCloudNudge == show)
        {
            OnPropertyChanged(nameof(MapToCloudNudgeText));
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        ShowMapToCloudNudge = show;
        OnPropertyChanged(nameof(ShowMapToCloudNudge));
        OnPropertyChanged(nameof(MapToCloudNudgeText));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Persist Notes root from the Notes setup CTA (Settings Browse uses SettingsViewModel).</summary>
    public void SetNotesRootPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        var full = Path.GetFullPath(path.Trim());
        var settings = _settingsService.LoadSettings();
        settings.NotesRootPath = full;
        _settingsService.SaveSettings(settings);
        ReloadFromSettings();
        StatusText = Directory.Exists(full)
            ? $"Notes folder: {full}"
            : "Notes folder not found. Choose another path.";
    }

    private void NotifyNotesListStateChanged()
    {
        OnPropertyChanged(nameof(NeedsNotesRoot));
        OnPropertyChanged(nameof(IsNotesEmpty));
        OnPropertyChanged(nameof(ShowNotesTree));
        RefreshMapToCloudNudge();
        CommandManager.InvalidateRequerySuggested();
    }

    private string? _lastTreeFingerprint;

    public void RefreshTree()
    {
        if (string.IsNullOrEmpty(_rootPath) || !Directory.Exists(_rootPath))
        {
            _lastTreeFingerprint = null;
            TreeRoot = null;
            CommandManager.InvalidateRequerySuggested();
            TreeRefreshed?.Invoke();
            RefreshSiblingFiles();
            return;
        }

        var path = _rootPath;
        // No WPF dispatcher (unit tests / headless): build on the caller thread.
        if (System.Windows.Application.Current?.Dispatcher == null)
        {
            ApplyBuiltTree(path);
            return;
        }

        _ = Task.Run(() => ApplyBuiltTree(path));
    }

    /// <summary>Re-enumerate Folder files under the open note (E5/E5a files-only). Soft-empty on errors.</summary>
    public void RefreshSiblingFiles()
    {
        if (string.IsNullOrEmpty(_currentPagePath))
        {
            SiblingTreeRoot = null;
            return;
        }

        try
        {
            var built = NotesSiblingFiles.BuildTree(_currentPagePath);
            SiblingTreeRoot = built != null ? new TreeItemViewModel(built) : null;
        }
        catch
        {
            SiblingTreeRoot = null;
            StatusText = "Could not list folder files.";
        }
    }

    private void SyncSiblingFilesCollection()
    {
        SiblingFiles.Clear();
        if (_siblingTreeRoot == null) return;
        foreach (var child in _siblingTreeRoot.Children)
            SiblingFiles.Add(child);
    }

    public void OpenSiblingPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusText = "File not found";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            StatusText = "Opened";
        }
        catch (Exception ex)
        {
            StatusText = "Could not open file.";
            ErrorDetailDialog.Show("Open file", ex);
        }
    }

    private void ApplyBuiltTree(string path)
    {
        try
        {
            var built = _fsService.BuildTree(path);
            var fingerprint = ComputeTreeFingerprint(built);
            PostUi(() =>
            {
                if (fingerprint != _lastTreeFingerprint || TreeRoot == null)
                {
                    _lastTreeFingerprint = fingerprint;
                    TreeRoot = built != null ? new TreeItemViewModel(built) : null;
                    CommandManager.InvalidateRequerySuggested();
                    TreeRefreshed?.Invoke();
                }

                // Always re-list Folder files — tree fingerprint ignore must not skip E5 panel.
                RefreshSiblingFiles();
            });
        }
        catch (Exception ex)
        {
            PostUi(() =>
            {
                _lastTreeFingerprint = null;
                TreeRoot = null;
                StatusText = "Error loading notes tree.";
                CommandManager.InvalidateRequerySuggested();
                TreeRefreshed?.Invoke();
                RefreshSiblingFiles();
                ErrorDetailDialog.Show("Notes tree", ex);
            });
        }
    }

    private static string ComputeTreeFingerprint(FileSystemItem? root)
    {
        if (root == null) return string.Empty;
        var entries = new List<string>();
        void Walk(FileSystemItem node, string relative)
        {
            entries.Add(relative + '\0' + node.Name);
            foreach (var child in node.Children)
            {
                var childRel = string.IsNullOrEmpty(relative)
                    ? child.Name
                    : relative + '/' + child.Name;
                Walk(child, childRel);
            }
        }
        Walk(root, string.Empty);
        entries.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join('\n', entries);
    }

    public async Task<string> LoadPageContentAsync(string folderPath)
    {
        string inner;
        if (!_fsService.HasIndexFile(folderPath))
            inner = "<p><br></p>";
        else
        {
            var indexPath = _fsService.GetIndexFilePath(folderPath);
            inner = await _fileService.OpenFileAsync(indexPath);
        }

        _currentHtml = inner;
        _htmlSyncedToDisk = inner;
        return _renderer.CreateEditableHtml(inner);
    }

    public async Task SavePageContentAsync(string folderPath, string html)
    {
        var indexPath = _fsService.GetWriteIndexFilePath(folderPath);
        try
        {
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            InternalNoteWriteStarting?.Invoke();
            await _fileService.SaveFileAsync(indexPath, html);
            PostUi(() =>
            {
                _htmlSyncedToDisk = html;
                _pendingDebouncedSave = false;
                StatusText = $"Saved at {DateTime.Now:HH:mm:ss}";
                ContentSaved?.Invoke(folderPath);
            });
        }
        catch (Exception ex)
        {
            PostUi(() =>
            {
                _pendingDebouncedSave = false;
                StatusText = "Error while saving. See details.";
                ErrorDetailDialog.Show("Error saving note", ex);
            });
        }
        finally
        {
            InternalNoteWriteCompleted?.Invoke();
        }
    }

    public void ScheduleSave(string folderPath, string html)
    {
        _saveCts?.Cancel();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;
        var scheduleOwner = _saveCts;
        var contentToSave = html;
        _pendingDebouncedSave = true;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                if (token.IsCancellationRequested) return;
                await SavePageContentAsync(folderPath, contentToSave);
            }
            catch (OperationCanceledException)
            {
                PostUi(() =>
                {
                    if (ReferenceEquals(_saveCts, scheduleOwner))
                        _pendingDebouncedSave = false;
                });
            }
            catch (Exception ex)
            {
                PostUi(() =>
                {
                    _pendingDebouncedSave = false;
                    StatusText = "Error while saving. See details.";
                    ErrorDetailDialog.Show("Error saving note", ex);
                });
            }
        }, token);
        PostUi(() => StatusText = "Changes will be saved in 5 seconds...");
    }

    public async Task HandleEditorContentChangedAsync(string html)
    {
        if (string.IsNullOrEmpty(CurrentPagePath))
            return;

        if (IsPlaceholderEmpty(html))
        {
            if (_currentHtml.Length > 0)
            {
                _currentHtml = string.Empty;
                ScheduleSave(CurrentPagePath, string.Empty);
            }
            return;
        }

        if (html == _currentHtml)
            return;

        var oldHtml = _currentHtml;
        _currentHtml = html;

        var pagePath = CurrentPagePath;
        var newPath = await Task.Run(() => _titleRename.TryRenameFromFirstH1Async(oldHtml, html, pagePath));
        if (!string.Equals(newPath, pagePath, StringComparison.OrdinalIgnoreCase))
        {
            CurrentPagePath = newPath;
            _htmlSyncedToDisk = html;
            RefreshTree();
            StatusText = $"Page renamed to: {Path.GetFileName(newPath)}";
            NotesPagePathChanged?.Invoke(newPath);
        }

        ScheduleSave(CurrentPagePath!, html);
    }

    private static bool IsPlaceholderEmpty(string html)
    {
        if (string.IsNullOrEmpty(html))
            return true;
        var t = html.Trim();
        return t == "<p><br></p>" || t == "<br>";
    }

    public bool RenameFolder(string fullPath, string newName)
    {
        if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(_rootPath))
            return false;
        if (string.Equals(Path.GetFullPath(fullPath), Path.GetFullPath(_rootPath), StringComparison.OrdinalIgnoreCase))
            return false;
        if (!fullPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            _fsService.RenameItem(fullPath, newName, isFolder: true);
            var parent = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(parent))
                return false;
            var newPath = Path.Combine(parent, newName);
            if (string.Equals(_currentPagePath, fullPath, StringComparison.OrdinalIgnoreCase))
                CurrentPagePath = newPath;
            RefreshTree();
            StatusText = "Renamed";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = "Error renaming.";
            ErrorDetailDialog.Show("Error renaming folder", ex);
            return false;
        }
    }

    /// <summary>
    /// Reparent a note folder under <paramref name="targetParentPath"/> (local FS). Name preserved.
    /// </summary>
    public bool MoveFolder(string sourcePath, string targetParentPath)
    {
        if (string.IsNullOrEmpty(_rootPath))
            return false;

        var error = NotesFileSystemService.ValidateMove(sourcePath, targetParentPath, _rootPath);
        if (error != null)
        {
            StatusText = error;
            return false;
        }

        try
        {
            var sourceFull = Path.GetFullPath(sourcePath);
            var destFull = _fsService.MoveFolder(sourceFull, targetParentPath);
            var pathChanged = RemapCurrentPagePathAfterMove(sourceFull, destFull);
            if (!pathChanged)
                CurrentPagePath = destFull;
            RefreshTree();
            StatusText = "Moved";
            if (pathChanged || !string.IsNullOrEmpty(CurrentPagePath))
                NotesPagePathChanged?.Invoke(CurrentPagePath!);
            return true;
        }
        catch (Exception ex)
        {
            StatusText = "Error moving.";
            ErrorDetailDialog.Show("Error moving folder", ex);
            return false;
        }
    }

    /// <returns>True when an open page path was remapped because it was the source or under it.</returns>
    private bool RemapCurrentPagePathAfterMove(string sourceFull, string destFull)
    {
        if (string.IsNullOrEmpty(_currentPagePath))
            return false;

        string currentFull;
        try
        {
            currentFull = Path.GetFullPath(_currentPagePath);
        }
        catch
        {
            return false;
        }

        sourceFull = sourceFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        currentFull = currentFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        destFull = destFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(currentFull, sourceFull, StringComparison.OrdinalIgnoreCase))
        {
            CurrentPagePath = destFull;
            return true;
        }

        var prefix = sourceFull + Path.DirectorySeparatorChar;
        if (!currentFull.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        CurrentPagePath = destFull + currentFull.Substring(sourceFull.Length);
        return true;
    }

    public void RaiseRequestNewFolder() => RequestNewFolder?.Invoke();

    public bool DeleteFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || string.IsNullOrEmpty(_rootPath)) return false;
        if (string.Equals(Path.GetFullPath(folderPath), Path.GetFullPath(_rootPath), StringComparison.OrdinalIgnoreCase))
            return false;
        if (!folderPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            // Resolve before delete so sibling order still includes the removed folder (AT-ND-1…3).
            var nextPath = NotesDeleteNavigate.ResolveNextPath(_rootPath, folderPath, _currentPagePath);
            var openAffected = NotesDeleteNavigate.IsOpenPageAffected(_currentPagePath, folderPath);

            _fsService.DeleteItem(folderPath, isFolder: true);

            if (openAffected)
                CurrentPagePath = nextPath;
            RefreshTree();
            StatusText = "Deleted";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = "Error deleting.";
            ErrorDetailDialog.Show("Error deleting folder", ex);
            return false;
        }
    }
}
