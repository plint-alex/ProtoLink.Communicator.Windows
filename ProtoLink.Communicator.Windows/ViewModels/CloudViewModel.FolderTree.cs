using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class CloudViewModel
{
    private Guid? _photosLibraryId;
    private Guid? _folderTreeRootId;
    private readonly Dictionary<Guid, string> _folderTreeCodes = new();
    private readonly Dictionary<Guid, IReadOnlyList<CloudFolderTree.Node>> _folderTreeChildren = new();
    private readonly Dictionary<Guid, string> _folderTreeNames = new();
    private readonly HashSet<Guid> _folderTreeExpanded = new();
    private readonly HashSet<Guid> _folderTreeLoaded = new();
    private readonly HashSet<Guid> _folderTreeLoading = new();
    private int _folderTreeFolderCount;

    public ObservableCollection<CloudFolderTreeRowViewModel> FolderTreeRows { get; } = new();

    public bool ShowFolderTree => FolderTreeRows.Count > 0;

    public string FolderTreeHeading =>
        ShareSheetStrings.FoldersTree(_settingsService.LoadSettings().UiLanguage);

    public ICommand OpenFolderTreeCommand { get; private set; } = null!;
    public ICommand ToggleFolderTreeExpandCommand { get; private set; } = null!;

    private void InitFolderTreeCommands()
    {
        OpenFolderTreeCommand = new RelayCommand(
            async p => await OpenFolderTreeRowAsync((CloudFolderTreeRowViewModel)p!),
            p => p is CloudFolderTreeRowViewModel);
        ToggleFolderTreeExpandCommand = new RelayCommand(
            async p => await ToggleFolderTreeExpandAsync((CloudFolderTreeRowViewModel)p!),
            p => p is CloudFolderTreeRowViewModel row && (row.CanExpand || row.IsLoading || row.IsExpanded));
    }

    private async Task EnsurePhotosLibraryIdAsync()
    {
        if (_photosLibraryId != null || _authService.CurrentToken == null) return;
        try
        {
            var userId = _authService.CurrentToken.UserId;
            var kids = await _api.GetEntitiesAsync(new[] { userId }, includeValues: true);
            _photosLibraryId = PhotosLibrary.ResolveLibraryIdFromUserChildren(kids);
        }
        catch
        {
            // Best-effort; hub Photos row omitted when unresolved.
        }
    }

    private void ApplyOptimisticFolderTreeSelection(Guid? folderId)
    {
        if (FolderTreeRows.Count == 0) return;
        foreach (var r in FolderTreeRows)
            r.IsSelected = folderId is { } id && r.Id == id;
    }

    private async Task OpenFolderTreeRowAsync(CloudFolderTreeRowViewModel row)
    {
        if (CurrentEntity?.Id == row.Id) return;

        // Paint tree selection immediately — never wait on ancestry GetEntities first.
        ApplyOptimisticFolderTreeSelection(row.Id);

        var treeRows = FolderTreeRows.Select(r => (r.Id, r.Name, r.Depth)).ToList();
        var ancestors = CloudFolderNavigation.BuildAncestorTrail(treeRows, row.Id);
        if (ancestors.Count > 0)
        {
            var composed = CloudFolderNavigation.ComposeBreadcrumb(
                BreadcrumbPath.Select(b => (b.Id, b.Name)).ToList(),
                ancestors);
            Guid? parentId = composed.Count >= 2 ? composed[^2].Id : null;
            var trail = composed
                .Select(c => new CloudBreadcrumbEntry { Id = c.Id, Name = c.Name })
                .ToList();
            await OpenAsync(
                new CloudEntityRef(row.Id, CloudEntityKind.Folder, row.Name, parentId),
                trail);
            EntityOpened?.Invoke();
            return;
        }

        BeginFolderListingLoad();
        await YieldForChromePaintAsync();
        if (await TryOpenEntityAsync(row.Id))
            EntityOpened?.Invoke();
    }

    private async Task ToggleFolderTreeExpandAsync(CloudFolderTreeRowViewModel row)
    {
        if (row.IsExpanded)
        {
            CloudFolderTree.CollapseBranch(row.Id, _folderTreeChildren, _folderTreeExpanded);
            _folderTreeLoading.Remove(row.Id);
            RebuildFolderTreeRows();
            return;
        }

        if (!row.CanExpand && !_folderTreeLoaded.Contains(row.Id))
            return;

        _folderTreeLoading.Add(row.Id);
        RebuildFolderTreeRows();
        await YieldForChromePaintAsync();

        try
        {
            if (!await TryEnsureFolderTreeChildrenLoadedAsync(row.Id))
                return; // soft-fail: leave unloaded so retry works; loading cleared in finally
            if (_folderTreeChildren.TryGetValue(row.Id, out var kids) && kids.Count > 0)
                _folderTreeExpanded.Add(row.Id);
        }
        finally
        {
            _folderTreeLoading.Remove(row.Id);
            RebuildFolderTreeRows();
        }
    }

    private async Task RefreshFolderTreeAsync()
    {
        if (ListedFolderId is not { } listed || CloudVirtualNodes.IsVirtual(listed))
        {
            ClearFolderTree();
            return;
        }

        await EnsurePhotosLibraryIdAsync();
        var crumbMeta = BreadcrumbPath
            .Select(b =>
            {
                _folderTreeCodes.TryGetValue(b.Id, out var code);
                if (b.Id == _myCloudRootId) code = CloudEntityCodes.CloudRoot;
                return (b.Id, code);
            })
            .ToList();

        var rootId = CloudFolderTree.PickTreeRootId(
            crumbMeta,
            _photosLibraryId,
            CloudVirtualNodes.IsVirtual);
        if (rootId is not { } treeRoot || treeRoot == Guid.Empty)
        {
            ClearFolderTree();
            return;
        }

        // Snapshot before Reset so soft-fail can restore last-good chrome (not empty first paint).
        var priorHadChrome = FolderTreeRows.Count > 0;
        var cacheSnap = SnapshotFolderTreeCache();
        var priorRootId = _folderTreeRootId;

        try
        {
            // Same root → keep user expands across listing refresh; new root → E9b first paint.
            var nextExpanded = CloudFolderTree.ExpandSetForRefresh(
                _folderTreeRootId,
                treeRoot,
                _folderTreeExpanded);
            ResetFolderTreeCache();
            foreach (var id in nextExpanded)
                _folderTreeExpanded.Add(id);

            var rootName = BreadcrumbPath.FirstOrDefault(b => b.Id == treeRoot)?.Name
                           ?? (_photosLibraryId == treeRoot
                               ? ShareSheetStrings.Photos(_settingsService.LoadSettings().UiLanguage)
                               : "Cloud");
            _folderTreeNames[treeRoot] = rootName;

            if (!await TryEnsureFolderTreeChildrenLoadedAsync(treeRoot))
            {
                ApplyFolderTreeSoftFailChrome(treeRoot, rootName, priorHadChrome, priorRootId, cacheSnap);
                return;
            }

            if (ListedFolderId is not { } still || CloudVirtualNodes.IsVirtual(still))
            {
                ClearFolderTree();
                return;
            }

            // Reload children for preserved expands so ProjectVisible can keep descendants visible.
            foreach (var id in _folderTreeExpanded.ToList())
            {
                if (id == treeRoot) continue;
                if (!await TryEnsureFolderTreeChildrenLoadedAsync(id))
                    continue; // soft-fail: leave unloaded; expand set stays for retry
            }

            _folderTreeRootId = treeRoot;
            RebuildFolderTreeRows();
        }
        catch (Exception ex) when (ApiExceptionHelper.IsExpectedConnectivityFailure(ex))
        {
            System.Diagnostics.Debug.WriteLine(ex);
            var rootName = BreadcrumbPath.FirstOrDefault(b => b.Id == treeRoot)?.Name
                           ?? (_photosLibraryId == treeRoot
                               ? ShareSheetStrings.Photos(_settingsService.LoadSettings().UiLanguage)
                               : "Cloud");
            ApplyFolderTreeSoftFailChrome(treeRoot, rootName, priorHadChrome, priorRootId, cacheSnap);
        }
    }

    /// <summary>
    /// After ResetFolderTreeCache + soft-fail: restore last-good cache/rows, or paint a root stub
    /// so ShowFolderTree stays true until the next successful listing refresh rebuilds.
    /// </summary>
    private void ApplyFolderTreeSoftFailChrome(
        Guid treeRoot,
        string rootName,
        bool priorHadChrome,
        Guid? priorRootId,
        FolderTreeCacheSnapshot cacheSnap)
    {
        if (priorHadChrome)
        {
            RestoreFolderTreeCache(cacheSnap);
            _folderTreeRootId = priorRootId;
            // FolderTreeRows still hold prior paint; do not Clear.
            return;
        }

        // First paint / empty after listing Clear: root-only chrome (unloaded → CanExpand).
        ResetFolderTreeCache();
        _folderTreeNames[treeRoot] = rootName;
        _folderTreeExpanded.Add(treeRoot);
        _folderTreeRootId = treeRoot;
        RebuildFolderTreeRows();
    }

    /// <summary>
    /// Listing catch: success-path RefreshFolderTreeAsync never runs when GetEntities fails first.
    /// Keep last-good rows; when empty, paint a root stub so ShowFolderTree is true under the banner.
    /// </summary>
    private void EnsureFolderTreeChromeOnListingSoftFail()
    {
        if (FolderTreeRows.Count > 0)
            return; // last-good chrome retained (no ClearFolderTree on catch)

        if (ListedFolderId is not { } listed || CloudVirtualNodes.IsVirtual(listed))
            return;

        var crumbMeta = BreadcrumbPath
            .Select(b =>
            {
                _folderTreeCodes.TryGetValue(b.Id, out var code);
                if (b.Id == _myCloudRootId) code = CloudEntityCodes.CloudRoot;
                return (b.Id, code);
            })
            .ToList();

        var treeRoot = CloudFolderTree.ResolveListingSoftFailStubRootId(
            crumbMeta,
            _photosLibraryId,
            listed,
            _myCloudRootId,
            CloudVirtualNodes.IsVirtual);
        if (treeRoot is not { } root || root == Guid.Empty)
            return;

        var rootName = BreadcrumbPath.FirstOrDefault(b => b.Id == root)?.Name
                       ?? (_photosLibraryId == root
                           ? ShareSheetStrings.Photos(_settingsService.LoadSettings().UiLanguage)
                           : "Cloud");
        ApplyFolderTreeSoftFailChrome(
            root,
            rootName,
            priorHadChrome: false,
            priorRootId: null,
            cacheSnap: SnapshotFolderTreeCache());
    }

    private FolderTreeCacheSnapshot SnapshotFolderTreeCache() =>
        new(
            _folderTreeChildren.ToDictionary(kv => kv.Key, kv => kv.Value),
            _folderTreeNames.ToDictionary(kv => kv.Key, kv => kv.Value),
            new HashSet<Guid>(_folderTreeExpanded),
            new HashSet<Guid>(_folderTreeLoaded),
            new HashSet<Guid>(_folderTreeLoading),
            _folderTreeFolderCount);

    private void RestoreFolderTreeCache(FolderTreeCacheSnapshot snap)
    {
        _folderTreeChildren.Clear();
        foreach (var kv in snap.Children)
            _folderTreeChildren[kv.Key] = kv.Value;
        _folderTreeNames.Clear();
        foreach (var kv in snap.Names)
            _folderTreeNames[kv.Key] = kv.Value;
        _folderTreeExpanded.Clear();
        foreach (var id in snap.Expanded)
            _folderTreeExpanded.Add(id);
        _folderTreeLoaded.Clear();
        foreach (var id in snap.Loaded)
            _folderTreeLoaded.Add(id);
        _folderTreeLoading.Clear();
        foreach (var id in snap.Loading)
            _folderTreeLoading.Add(id);
        _folderTreeFolderCount = snap.FolderCount;
    }

    private sealed record FolderTreeCacheSnapshot(
        Dictionary<Guid, IReadOnlyList<CloudFolderTree.Node>> Children,
        Dictionary<Guid, string> Names,
        HashSet<Guid> Expanded,
        HashSet<Guid> Loaded,
        HashSet<Guid> Loading,
        int FolderCount);

    /// <summary>
    /// Loads children for <paramref name="parentId"/>. Returns false on expected connectivity
    /// failure (timeout/cancel/IO) without marking the parent loaded — retry on next expand.
    /// </summary>
    private async Task<bool> TryEnsureFolderTreeChildrenLoadedAsync(Guid parentId)
    {
        try
        {
            await EnsureFolderTreeChildrenLoadedAsync(parentId);
            return true;
        }
        catch (Exception ex) when (ApiExceptionHelper.IsExpectedConnectivityFailure(ex))
        {
            // Soft-fail: do not add to _folderTreeLoaded; clear loading at call-site finally.
            System.Diagnostics.Debug.WriteLine(ex);
            return false;
        }
    }

    private async Task EnsureFolderTreeChildrenLoadedAsync(Guid parentId)
    {
        if (_folderTreeLoaded.Contains(parentId)) return;
        if (_folderTreeFolderCount >= CloudFolderTree.DefaultMaxFolders)
        {
            _folderTreeChildren[parentId] = Array.Empty<CloudFolderTree.Node>();
            _folderTreeLoaded.Add(parentId);
            return;
        }

        // Values required so GetDisplayName resolves folder name slots (not folder-{id}).
        var list = await _api.GetEntitiesAsync(new[] { parentId }, includeValues: true);
        var folderKids = new List<CloudFolderTree.Node>();
        foreach (var e in (list ?? new List<GetEntitiesResult>())
                 .Where(x => CloudFolderTree.IsFolderTreeCode(x.Code))
                 .OrderBy(x => CloudApiService.GetDisplayName(x), StringComparer.OrdinalIgnoreCase))
        {
            if (_folderTreeFolderCount >= CloudFolderTree.DefaultMaxFolders)
                break;
            _folderTreeCodes[e.Id] = e.Code ?? CloudEntityCodes.CloudFolder;
            var name = CloudApiService.GetDisplayName(e);
            _folderTreeNames[e.Id] = name;
            folderKids.Add(new CloudFolderTree.Node(e.Id, name));
            _folderTreeFolderCount++;
        }

        _folderTreeChildren[parentId] = folderKids;
        _folderTreeLoaded.Add(parentId);
    }

    private void RebuildFolderTreeRows()
    {
        if (_folderTreeRootId is not { } treeRoot || treeRoot == Guid.Empty)
        {
            if (FolderTreeRows.Count > 0)
            {
                FolderTreeRows.Clear();
                OnPropertyChanged(nameof(ShowFolderTree));
            }
            return;
        }

        var selected = CloudFolderTree.SelectedFolderId(
            IsFileOpen,
            CurrentEntity?.Id,
            CurrentEntity?.ParentId);
        var rootName = _folderTreeNames.GetValueOrDefault(treeRoot, "Cloud");
        var visible = CloudFolderTree.ProjectVisible(
            new CloudFolderTree.Node(treeRoot, rootName),
            _folderTreeChildren,
            _folderTreeExpanded,
            _folderTreeLoaded,
            selected,
            _folderTreeLoading);

        FolderTreeRows.Clear();
        foreach (var row in visible)
        {
            FolderTreeRows.Add(new CloudFolderTreeRowViewModel
            {
                Id = row.Id,
                Name = row.Name,
                Depth = row.Depth,
                IsSelected = row.Selected,
                IsExpanded = row.IsExpanded,
                CanExpand = row.CanExpand,
                IsLoading = row.IsLoading
            });
        }

        OnPropertyChanged(nameof(ShowFolderTree));
        OnPropertyChanged(nameof(FolderTreeHeading));
    }

    private void ResetFolderTreeCache()
    {
        _folderTreeChildren.Clear();
        _folderTreeNames.Clear();
        _folderTreeExpanded.Clear();
        _folderTreeLoaded.Clear();
        _folderTreeLoading.Clear();
        _folderTreeFolderCount = 0;
    }

    private void ClearFolderTree()
    {
        if (FolderTreeRows.Count == 0 && _folderTreeRootId == null) return;
        FolderTreeRows.Clear();
        _folderTreeRootId = null;
        ResetFolderTreeCache();
        OnPropertyChanged(nameof(ShowFolderTree));
    }
}
