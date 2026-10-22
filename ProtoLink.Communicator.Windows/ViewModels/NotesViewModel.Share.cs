using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Utilities;
using ProtoLink.Communicator.Windows.ViewModels.Share;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class NotesViewModel
{
    private CloudApiService? _shareApi;
    private Func<Guid?>? _shareCurrentUserId;
    private Func<bool>? _shareIsOnline;
    private readonly Dictionary<Guid, bool> _externalShareByRemoteId = new();
    private int _sharePrefetchBusy;

    public ShareSheetViewModel? ShareSheet { get; private set; }

    public ICommand OpenShareCommand { get; private set; } = null!;

    /// <summary>Raised when lazy share-affordance cache changes (tree glyphs).</summary>
    public event Action? ShareAffordanceChanged;

    private void InitShare(
        CloudApiService? shareApi,
        Func<Guid?>? currentUserId,
        Func<bool>? isOnline,
        Action<string>? onSendShareLinkInMessenger)
    {
        _shareApi = shareApi;
        _shareCurrentUserId = currentUserId;
        _shareIsOnline = isOnline;

        if (shareApi == null)
        {
            OpenShareCommand = new RelayCommand(_ => { }, _ => false);
            return;
        }

        ShareSheet = new ShareSheetViewModel(
            shareApi,
            () => _settingsService.LoadSettings().UiLanguage,
            () => _shareCurrentUserId?.Invoke(),
            s => StatusText = s,
            onSendShareLinkInMessenger);
        ShareSheet.CopyCanonicalLink = () =>
        {
            var id = ShareSheet.EntityId;
            if (id == Guid.Empty) return;
            try
            {
                System.Windows.Clipboard.SetText(BuildLink(id));
                StatusText = "Link copied";
            }
            catch (Exception ex)
            {
                StatusText = "Could not copy link: " + ex.Message;
            }
        };
        ShareSheet.GetCanonicalLink = () =>
            ShareSheet.EntityId == Guid.Empty ? null : BuildLink(ShareSheet.EntityId);

        ShareSheet.PropertyChanged += OnShareSheetPropertyChanged;
        OpenShareCommand = new RelayCommand(
            async p => await OpenShareForPathAsync(p as string),
            p => CanOpenShareForPath(p as string));
    }

    /// <summary>Same E2 gate as Copy link / Open in browser (AC-NS-1 / AT-S47).</summary>
    public bool CanOpenShareForPath(string? noteFolderFullPath) =>
        ShareSheet != null && TryResolveRemoteId(noteFolderFullPath).HasValue;

    public bool ShowsExternalShareAffordance(string? noteFolderFullPath)
    {
        var id = TryResolveRemoteId(noteFolderFullPath);
        return id is { } remoteId
               && _externalShareByRemoteId.TryGetValue(remoteId, out var shared)
               && shared;
    }

    public void RememberExternalShare(Guid remoteId, bool hasExternal)
    {
        if (_externalShareByRemoteId.TryGetValue(remoteId, out var prev) && prev == hasExternal)
            return;
        _externalShareByRemoteId[remoteId] = hasExternal;
        ShareAffordanceChanged?.Invoke();
    }

    public async Task OpenShareForPathAsync(string? noteFolderFullPath)
    {
        if (ShareSheet == null) return;
        var id = TryResolveRemoteId(noteFolderFullPath);
        if (id is not { } remoteId)
        {
            StatusText = ShareSheetStrings.NotInCloudYet(_settingsService.LoadSettings().UiLanguage);
            return;
        }

        var name = string.IsNullOrWhiteSpace(noteFolderFullPath)
            ? HeaderTitle
            : Path.GetFileName(noteFolderFullPath) ?? remoteId.ToString("D");
        var online = _shareIsOnline?.Invoke() ?? true;

        await ShareSheet.OpenAsync(
            remoteId,
            name,
            isFolder: true,
            childCount: 0,
            canEdit: true,
            isOnline: online);
        CaptureShareAffordanceFromOpenSheet();
        OnPropertyChanged(nameof(ShareSheet));
    }

    /// <summary>
    /// Lazy prefetch for one Cloud-linked path when the context menu opens (no full-tree scan).
    /// </summary>
    public async Task PrefetchShareAffordanceAsync(string? noteFolderFullPath)
    {
        if (_shareApi == null) return;
        var id = TryResolveRemoteId(noteFolderFullPath);
        if (id is not { } remoteId) return;
        if (_externalShareByRemoteId.ContainsKey(remoteId)) return;
        if (_shareIsOnline?.Invoke() == false) return;
        if (Interlocked.CompareExchange(ref _sharePrefetchBusy, 1, 0) != 0) return;

        try
        {
            var linksTask = _shareApi.ListShareLinksAsync(remoteId);
            var grantsTask = _shareApi.ListShareGrantsAsync(remoteId);
            await Task.WhenAll(linksTask, grantsTask);
            var live = (await linksTask).Count(l => l.IsLive);
            var has = NotesTreeShare.HasExternalShare(
                await grantsTask,
                live,
                _shareCurrentUserId?.Invoke());
            RememberExternalShare(remoteId, has);
        }
        catch
        {
            // Offline / error: omit glyph rather than invent “shared”.
        }
        finally
        {
            Interlocked.Exchange(ref _sharePrefetchBusy, 0);
        }
    }

    private void OnShareSheetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShareSheetViewModel.IsLoading)) return;
        if (ShareSheet is not { IsOpen: true, IsLoading: false }) return;
        CaptureShareAffordanceFromOpenSheet();
    }

    private void CaptureShareAffordanceFromOpenSheet()
    {
        if (ShareSheet is not { IsOpen: true } sheet || sheet.EntityId == Guid.Empty)
            return;

        var grants = sheet.People.Select(p => p.Grant).ToList();
        var has = NotesTreeShare.HasExternalShare(
            grants,
            sheet.LiveLinks.Count,
            _shareCurrentUserId?.Invoke());
        RememberExternalShare(sheet.EntityId, has);
    }
}
