using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using ProtoLink.Communicator.Windows.Navigation;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class NotesViewModel
{
    private readonly NavigationHistory<string> _history = new();
    private bool _isCompactLayout;
    private bool _showEditorInCompact;
    private Guid? _currentRemoteId;

    public bool IsCompactLayout
    {
        get => _isCompactLayout;
        set
        {
            if (_isCompactLayout == value) return;
            _isCompactLayout = value;
            if (!_isCompactLayout)
                _showEditorInCompact = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(ShowBack));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool ShowEditorInCompact
    {
        get => _showEditorInCompact;
        set
        {
            if (_showEditorInCompact == value) return;
            _showEditorInCompact = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(ShowBack));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool CanGoBack => _history.CanGoBack || (IsCompactLayout && ShowEditorInCompact);
    public bool ShowBack => CanGoBack;

    public string HeaderTitle =>
        string.IsNullOrEmpty(CurrentPagePath) ? "Select a note" : Path.GetFileName(CurrentPagePath);

    public string HeaderSubtitle
    {
        get
        {
            if (string.IsNullOrEmpty(CurrentPagePath) || string.IsNullOrEmpty(RootPath))
                return string.Empty;
            var parent = Path.GetDirectoryName(CurrentPagePath);
            if (string.IsNullOrEmpty(parent))
                return string.Empty;
            if (string.Equals(Path.GetFullPath(parent), Path.GetFullPath(RootPath), StringComparison.OrdinalIgnoreCase))
                return "… › Notes";
            return "… › " + Path.GetFileName(parent);
        }
    }

    public bool CanNavigateParent
    {
        get
        {
            if (string.IsNullOrEmpty(CurrentPagePath) || string.IsNullOrEmpty(RootPath))
                return false;
            var parent = Path.GetDirectoryName(CurrentPagePath);
            if (string.IsNullOrEmpty(parent))
                return false;
            var parentFull = Path.GetFullPath(parent);
            var rootFull = Path.GetFullPath(RootPath);
            return parentFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
        }
    }

    public Guid? CurrentRemoteId
    {
        get => _currentRemoteId;
        private set
        {
            if (_currentRemoteId == value) return;
            _currentRemoteId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCloudLink));
            OnPropertyChanged(nameof(CopyLinkToolTip));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HasCloudLink => CurrentRemoteId.HasValue;
    public string CopyLinkToolTip => HasCloudLink ? "Copy link (Ctrl+Shift+C)" : "Not in Cloud yet";

    public ICommand BackCommand { get; private set; } = null!;
    public ICommand CopyLinkCommand { get; private set; } = null!;
    public ICommand OpenInBrowserCommand { get; private set; } = null!;
    public ICommand NavigateParentCommand { get; private set; } = null!;

    /// <summary>Raised when Back/parent navigation needs the view to load a note path.</summary>
    public event Action<string>? NavigateToNoteRequested;
    public event Action? ReturnToListRequested;

    private void InitNavigationCommands()
    {
        BackCommand = new RelayCommand(_ => HandleBack(), _ => CanGoBack);
        // Parameter: note folder full path (tree menu) or null for the open page (Ctrl+Shift+C).
        CopyLinkCommand = new RelayCommand(p => CopyLinkForPath(p as string), p => ResolveLinkTargetId(p as string).HasValue);
        OpenInBrowserCommand = new RelayCommand(p => OpenInBrowserForPath(p as string), p => ResolveLinkTargetId(p as string).HasValue);
        NavigateParentCommand = new RelayCommand(_ => NavigateParent(), _ => CanNavigateParent);
        _history.Changed += () =>
        {
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(ShowBack));
            CommandManager.InvalidateRequerySuggested();
        };
    }

    /// <summary>Call when the view opens a note (tree click / create / rename). Pushes history for the previous page.</summary>
    public void RecordOpenedPage(string? previousPath, string newPath, bool pushHistory = true)
    {
        if (pushHistory
            && !string.IsNullOrEmpty(previousPath)
            && !string.Equals(previousPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            _history.Push(new NavigationEntry<string>(previousPath));
        }

        RefreshRemoteLinkState();
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(CanNavigateParent));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Re-resolve E2 id after sync completes without reopening the note (AT-N3).</summary>
    public void RefreshRemoteLinkState()
    {
        if (string.IsNullOrEmpty(CurrentPagePath) || _remoteIdResolver == null)
        {
            CurrentRemoteId = null;
            return;
        }

        CurrentRemoteId = _remoteIdResolver.Resolve(CurrentPagePath);
    }

    /// <summary>E2 Cloud RemoteId for any note folder path (tree context menu Copy id). Null if unmapped / not synced.</summary>
    public Guid? TryResolveRemoteId(string? noteFolderFullPath)
    {
        if (string.IsNullOrWhiteSpace(noteFolderFullPath) || _remoteIdResolver == null)
            return null;
        try
        {
            return _remoteIdResolver.Resolve(noteFolderFullPath);
        }
        catch
        {
            // Tree paint / share glyph must never throw (silent MainWindow P0).
            return null;
        }
    }

    public void ReturnToListInCompact()
    {
        ShowEditorInCompact = false;
        ReturnToListRequested?.Invoke();
    }

    private void HandleBack()
    {
        if (_history.TryPop(out var entry) && !string.IsNullOrEmpty(entry.Key))
        {
            NavigateToNoteRequested?.Invoke(entry.Key);
            return;
        }

        if (IsCompactLayout && ShowEditorInCompact)
            ReturnToListInCompact();
    }

    private void NavigateParent()
    {
        if (string.IsNullOrEmpty(CurrentPagePath))
            return;
        var parent = Path.GetDirectoryName(CurrentPagePath);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            return;
        NavigateToNoteRequested?.Invoke(parent);
    }

    private string BuildLink(Guid entityId)
    {
        var settings = _settingsService.LoadSettings();
        return EntityLinks.Build(settings.PublicSiteBaseUrl, entityId, settings.UiLanguage);
    }

    /// <summary>
    /// Canonical URL for a note folder path, or for the open page when <paramref name="noteFolderFullPath"/> is null/empty.
    /// Null when the note is not synced (no fake URL).
    /// </summary>
    public string? TryBuildCanonicalLink(string? noteFolderFullPath)
    {
        var id = ResolveLinkTargetId(noteFolderFullPath);
        return id is { } remoteId ? BuildLink(remoteId) : null;
    }

    private Guid? ResolveLinkTargetId(string? noteFolderFullPath) =>
        !string.IsNullOrWhiteSpace(noteFolderFullPath)
            ? TryResolveRemoteId(noteFolderFullPath)
            : CurrentRemoteId;

    /// <summary>Copy canonical link for a tree note path, or the open page when path is null.</summary>
    public void CopyLinkForPath(string? noteFolderFullPath)
    {
        var id = ResolveLinkTargetId(noteFolderFullPath);
        if (id is not { } remoteId)
        {
            StatusText = "Not in Cloud yet";
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(BuildLink(remoteId));
            StatusText = "Link copied";
        }
        catch (Exception ex)
        {
            StatusText = "Could not copy link: " + ex.Message;
        }
    }

    /// <summary>Open canonical URL for a tree note path, or the open page when path is null.</summary>
    public void OpenInBrowserForPath(string? noteFolderFullPath)
    {
        var id = ResolveLinkTargetId(noteFolderFullPath);
        if (id is not { } remoteId)
        {
            StatusText = "Not in Cloud yet";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = BuildLink(remoteId), UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = "Could not open browser: " + ex.Message;
        }
    }
}
