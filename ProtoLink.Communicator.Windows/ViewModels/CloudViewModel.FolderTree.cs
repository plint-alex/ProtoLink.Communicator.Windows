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
    private int _folderTreeRefreshGeneration;
    private bool _folderTreeRefreshRunning;
    private bool _folderTreeRefreshQueued;

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
            // Values required: Photos host embeds library Guid in StringValues (not code alone).
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

        // Commit expand intent before await so overlapping RefreshFolderTreeAsync cannot
        // snapshot-wipe this row after load settles (1.0.97 expand→load→collapse regress).
        _folderTreeLoading.Add(row.Id);
        _folderTreeExpanded.Add(row.Id);
        RebuildFolderTreeRows();
        await YieldForChromePaintAsync();

        try
        {
            if (!await TryEnsureFolderTreeChildrenLoadedAsync(row.Id))
            {
                _folderTreeExpanded.Remove(row.Id); // soft-fail: retry from collapsed
                return;
            }

            if (!_folderTreeChildren.TryGetValue(row.Id, out var kids) || kids.Count == 0)
                _folderTreeExpanded.Remove(row.Id); // leaf after load
        }
        finally
        {
            _folderTreeLoading.Remove(row.Id);
            RebuildFolderTreeRows();
        }
    }

    private async Task RefreshFolderTreeAsync()
    {
        // Coalesce overlapping listing-driven refreshes so a stale in-flight pass cannot
        // Rebuild with an expand snapshot taken before a concurrent Toggle committed.
        if (_folderTreeRefreshRunning)
        {
            _folderTreeRefreshQueued = true;
            return;
        }

        _folderTreeRefreshRunning = true;
        try
        {
            do
            {
                _folderTreeRefreshQueued = false;
                await RefreshFolderTreeCoreAsync().ConfigureAwait(true);
            } while (_folderTreeRefreshQueued);
        }
        finally
        {
            _folderTreeRefreshRunning = false;
        }
    }

    private async Task RefreshFolderTreeCoreAsync()
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

        // Snapshot before cache mutation so soft-fail can restore last-good chrome (not empty first paint).
        var priorHadChrome = FolderTreeRows.Count > 0;
        var cacheSnap = SnapshotFolderTreeCache();
        var priorRootId = _folderTreeRootId;
        var sameRoot = CloudFolderTree.KeepChildrenAcrossRefresh(priorRootId, treeRoot);
        var refreshGen = ++_folderTreeRefreshGeneration;

        try
        {
            // Same root → keep live expand set + prior children (re-fetch via cleared Loaded);
            // new root / first paint → E9b root-only via ExpandSetForRefresh + full Reset.
            var baselineExpanded = CloudFolderTree.ExpandSetForRefresh(
                _folderTreeRootId,
                treeRoot,
                _folderTreeExpanded);
            if (sameRoot)
            {
                _folderTreeLoaded.Clear();
                _folderTreeLoading.Clear();
                _folderTreeExpanded.Add(treeRoot);
            }
            else
            {
                ResetFolderTreeCache();
                foreach (var id in baselineExpanded)
                    _folderTreeExpanded.Add(id);
            }

            var rootName = BreadcrumbPath.FirstOrDefault(b => b.Id == treeRoot)?.Name
                           ?? (_photosLibraryId == treeRoot
                               ? ShareSheetStrings.Photos(_settingsService.LoadSettings().UiLanguage)
                               : "Cloud");
            _folderTreeNames[treeRoot] = rootName;

            if (!await TryEnsureFolderTreeChildrenLoadedAsync(treeRoot))
            {
                if (refreshGen != _folderTreeRefreshGeneration) return;
                ApplyFolderTreeSoftFailChrome(treeRoot, rootName, priorHadChrome, priorRootId, cacheSnap);
                return;
            }

            if (ListedFolderId is not { } still || CloudVirtualNodes.IsVirtual(still))
            {
                ClearFolderTree();
                return;
            }

            // Reload children for expands (incl. Toggle commits that landed during awaits).
            await EnsureExpandedFolderChildrenLoadedAsync(treeRoot).ConfigureAwait(true);
            if (refreshGen != _folderTreeRefreshGeneration) return;

            // Union again in case Toggle expanded while the load loop ran (never Clear live set).
            foreach (var id in CloudFolderTree.UnionExpandSets(
                         baselineExpanded,
                         _folderTreeExpanded,
                         treeRoot))
                _folderTreeExpanded.Add(id);
            await EnsureExpandedFolderChildrenLoadedAsync(treeRoot).ConfigureAwait(true);
            if (refreshGen != _folderTreeRefreshGeneration) return;

            _folderTreeRootId = treeRoot;
            RebuildFolderTreeRows();
        }
        catch (Exception ex) when (ApiExceptionHelper.IsExpectedConnectivityFailure(ex))
        {
            if (refreshGen != _folderTreeRefreshGeneration) return;
            System.Diagnostics.Debug.WriteLine(ex);
            var rootName = BreadcrumbPath.FirstOrDefault(b => b.Id == treeRoot)?.Name
                           ?? (_photosLibraryId == treeRoot
                               ? ShareSheetStrings.Photos(_settingsService.LoadSettings().UiLanguage)
                               : "Cloud");
            ApplyFolderTreeSoftFailChrome(treeRoot, rootName, priorHadChrome, priorRootId, cacheSnap);
        }
    }

    /// <summary>Load children for every expanded non-root id that is not yet loaded.</summary>
    private async Task EnsureExpandedFolderChildrenLoadedAsync(Guid treeRoot)
    {
        // Two passes: Toggle may add expands while the first pass awaits GetEntities.
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var id in _folderTreeExpanded.ToList())
            {
                if (id == treeRoot) continue;
                if (_folderTreeLoaded.Contains(id)) continue;
                if (!await TryEnsureFolderTreeChildrenLoadedAsync(id))
                    continue; // soft-fail: leave unloaded; expand set stays for retry
            }
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
            // Keep expands Toggle committed after the soft-fail snapshot was taken.
            var liveExpanded = new HashSet<Guid>(_folderTreeExpanded);
            RestoreFolderTreeCache(cacheSnap);
            foreach (var id in liveExpanded)
                _folderTreeExpanded.Add(id);
            _folderTreeRootId = priorRootId;
            // Mid-await Rebuild may have wiped rows while children were cleared — repaint from cache.
            RebuildFolderTreeRows();
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

        // Values-light: do not stack IncludeValues with interactive listing / hydrate (1.0.93 FAIL).
        // ResolveRowName fills sync-meta leaf names; synthetic folder-{id} is OK until hydrate.
        var list = await _api.GetEntitiesAsync(
            new[] { parentId },
            includeValues: false,
            maxPages: CloudApiService.InteractiveListingMaxPages,
            pageSize: CloudApiService.InteractiveListingPageSize);

        // Same-root refresh keeps prior kids until replace — drop their count before re-add.
        if (_folderTreeChildren.TryGetValue(parentId, out var priorKids) && priorKids != null)
            _folderTreeFolderCount = Math.Max(0, _folderTreeFolderCount - priorKids.Count);

        var folderKids = new List<CloudFolderTree.Node>();
        foreach (var e in (list ?? new List<GetEntitiesResult>())
                 .Where(x => CloudFolderTree.IsFolderTreeCode(x.Code))
                 .OrderBy(x => CloudInteractiveListing.ResolveRowName(x, ResolveListingMetaName),
                     StringComparer.OrdinalIgnoreCase))
        {
            if (_folderTreeFolderCount >= CloudFolderTree.DefaultMaxFolders)
                break;
            _folderTreeCodes[e.Id] = e.Code ?? CloudEntityCodes.CloudFolder;
            var name = CloudInteractiveListing.ResolveRowName(e, ResolveListingMetaName);
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
