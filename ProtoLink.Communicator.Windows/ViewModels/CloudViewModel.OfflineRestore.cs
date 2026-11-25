using System.Linq;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class CloudViewModel
{
    /// <summary>
    /// Cold start: restore <see cref="AppSettings.LastOpenCloudEntityId"/> when it still exists;
    /// otherwise open Cloud root with no error dialog (AT-C7).
    /// </summary>
    /// <summary>E4 — open a Cloud folder/file by id (messenger unfurl / deep link). Returns false when not navigable.</summary>
    public async Task<bool> TryOpenEntityAsync(Guid entityId)
    {
        if (_authService.CurrentToken == null) return false;
        try
        {
            var resolved = await TryResolveEntityPathAsync(entityId);
            if (resolved is not { } open)
            {
                // Paused Photos host with no My Cloud yet → Cloud hub (not blank host chrome).
                if (!PhotosLibrary.IsSurfaceEnabled())
                {
                    var found = await _api.GetEntitiesByIdsAsync(new[] { entityId }, includeValues: false);
                    if (found.FirstOrDefault() is { } e
                        && string.Equals(e.Code, PhotosLibrary.HostCode, StringComparison.Ordinal))
                    {
                        await EnsureRootAsync();
                        return true;
                    }
                }

                return false;
            }

            await OpenAsync(open.Entity, open.Breadcrumb, pushHistory: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task InitializeNavigationAsync()
    {
        if (_authService.CurrentToken == null) return;

        // Resolve canonical My Cloud before LastOpen so a Drive-style duplicate CloudRoot is not hub target.
        try
        {
            var userId = _authService.CurrentToken.UserId;
            var underUser = await _api.GetEntitiesAsync(new[] { userId }, includeValues: false);
            await ApplyCanonicalCloudRootFromUserChildrenAsync(underUser, userId).ConfigureAwait(true);
        }
        catch
        {
            // Sick Api: LastOpen listing-disk open / EnsureRoot soft-fail below.
        }

        var lastId = _settingsService.LoadSettings().LastOpenCloudEntityId;
        if (lastId is { } id)
        {
            try
            {
                var resolved = await TryResolveEntityPathAsync(id);
                var exists = resolved != null;
                if (CloudEntityRestore.Decide(lastId, exists) == CloudEntityRestore.Decision.OpenEntity
                    && resolved is { } open)
                {
                    // Skip restoring onto a non-canonical CloudRoot when duplicates exist (ops 2026-10-28).
                    var openCode = _folderTreeCodes.TryGetValue(open.Entity.Id, out var c) ? c : null;
                    if (CloudRootSelection.ShouldSkipLastOpenCloudRoot(
                            openCode,
                            open.Entity.Id,
                            _myCloudRootId == Guid.Empty ? null : _myCloudRootId,
                            _knownCloudRootIds.Count,
                            entityIsKnownCloudRoot: _knownCloudRootIds.Contains(open.Entity.Id)))
                    {
                        await EnsureRootOrListingDiskAsync(id);
                        return;
                    }

                    _history.Clear();
                    _locations.Clear();
                    await OpenAsync(open.Entity, open.Breadcrumb, pushHistory: false);
                    return;
                }
            }
            catch
            {
                // Silent — prefer listing-disk LastOpen before EnsureRoot (CACHE_PAINT_CLOUD).
            }

            if (await TryOpenLastOpenFromListingDiskAsync(id).ConfigureAwait(true))
                return;
        }

        await EnsureRootOrListingDiskAsync(lastId);
    }

    /// <summary>
    /// EnsureRoot when Api is reachable; on sick soft-fail open LastOpen from listing disk
    /// (never leave blank Loading with CurrentEntity null).
    /// </summary>
    private async Task EnsureRootOrListingDiskAsync(Guid? lastOpenId)
    {
        try
        {
            await EnsureRootAsync().ConfigureAwait(true);
        }
        catch
        {
            if (lastOpenId is { } id
                && await TryOpenLastOpenFromListingDiskAsync(id).ConfigureAwait(true))
                return;
            throw;
        }
    }

    /// <summary>
    /// Sick/cold: open LastOpen folder from CloudListingDiskCache without Api ancestry resolve.
    /// </summary>
    private async Task<bool> TryOpenLastOpenFromListingDiskAsync(Guid lastOpenId)
    {
        if (lastOpenId == Guid.Empty) return false;
        var cached = _listingDiskCache.TryLoad(lastOpenId);
        if (!PreferOnlineUiCache.ShouldOpenLastOpenFromListingDisk(cached.Count > 0))
            return false;

        if (_myCloudRootId == Guid.Empty || _myCloudRootId == lastOpenId)
        {
            _myCloudRootId = lastOpenId;
            _knownCloudRootIds.Add(lastOpenId);
            _folderTreeCodes[lastOpenId] = CloudEntityCodes.CloudRoot;
        }
        else
        {
            _folderTreeCodes.TryAdd(lastOpenId, CloudEntityCodes.CloudFolder);
        }

        var lang = _settingsService.LoadSettings().UiLanguage;
        var open = CloudEntityRestore.BuildOfflineMyCloudOpen(
            lastOpenId,
            ShareSheetStrings.CloudHub(lang),
            ShareSheetStrings.MyCloud(lang));
        _history.Clear();
        _locations.Clear();
        await OpenAsync(open.Entity, open.Breadcrumb, pushHistory: false).ConfigureAwait(true);
        return true;
    }

    private async Task<(CloudEntityRef Entity, IReadOnlyList<CloudBreadcrumbEntry> Breadcrumb)?> TryResolveEntityPathAsync(
        Guid entityId)
    {
        // Values-light resolve: IncludeValues ancestry walks contended Interactive GetEntities before
        // LastOpen listing (1.0.93 MyNotesFolder STUCK). Names from sync-meta / synthetic until hydrate.
        var found = await _api.GetEntitiesByIdsAsync(new[] { entityId }, includeValues: false);
        var entity = found.FirstOrDefault();
        if (entity == null)
            return null;

        // Photos host: paused → soft-land My Cloud / hub; else anti-SAP (AT-PC6) → library.
        if (string.Equals(entity.Code, PhotosLibrary.HostCode, StringComparison.Ordinal))
        {
            if (!PhotosLibrary.IsSurfaceEnabled())
            {
                if (_myCloudRootId != Guid.Empty && _myCloudRootId != entityId)
                    return await TryResolveEntityPathAsync(_myCloudRootId);
                return null;
            }

            await EnsurePhotosLibraryIdAsync();
            if (_photosLibraryId is { } lib && lib != entityId)
                return await TryResolveEntityPathAsync(lib);
            return null;
        }

        var knownRoot = _knownCloudRootIds.Contains(entity.Id);
        if (!CloudEntityRestore.IsNavigableCloudEntity(entity.TypeEntityId, entity.Code, knownRoot))
            return null;

        var chain = new List<CloudEntityRestore.AncestorNode>();
        var current = entity;
        Guid? currentMainParentId = null;
        try
        {
            var card = await _api.GetEntityCardAsync(entityId);
            currentMainParentId = card?.MainParentId;
        }
        catch
        {
            // Fall back to first Cloud folder/root parent.
        }

        for (var guard = 0; guard < 64; guard++)
        {
            var parents = await _api.GetParentEntitiesAsync(current.Id, includeValues: false);
            var isRoot = CloudEntityClassify.IsCloudRootCode(current.Code)
                         || _knownCloudRootIds.Contains(current.Id)
                         || parents.Any(p => p.Id == CloudEntityCodes.RootKindEntityId);

            var chainCode = isRoot ? CloudEntityCodes.CloudRoot : (current.Code ?? "");
            var display = isRoot
                ? "Cloud"
                : CloudInteractiveListing.ResolveRowName(current, ResolveListingMetaName);
            chain.Insert(0, new CloudEntityRestore.AncestorNode(current.Id, chainCode, display));
            _folderTreeCodes[current.Id] = chainCode;
            if (isRoot)
                break;

            var parent = CloudMainParent.PickParent(currentMainParentId, parents);
            if (parent == null || !await IsCloudTreeAncestorAsync(parent).ConfigureAwait(true))
                return null;

            Guid? nextMain = null;
            try
            {
                var parentCard = await _api.GetEntityCardAsync(parent.Id);
                nextMain = parentCard?.MainParentId;
            }
            catch
            {
                // ignore
            }

            current = parent;
            currentMainParentId = nextMain;
        }

        var open = CloudEntityRestore.BuildOpenTarget(chain);
        if (open is null) return null;

        // Remember ancestor CloudRoot for tree codes; do not override canonical My Cloud when duplicates exist.
        if (CloudEntityClassify.IsCloudRootCode(chain[0].Code))
        {
            _knownCloudRootIds.Add(chain[0].Id);
            _folderTreeCodes[chain[0].Id] = CloudEntityCodes.CloudRoot;
            if (_myCloudRootId == Guid.Empty
                || !_knownCloudRootIds.Contains(_myCloudRootId)
                || _knownCloudRootIds.Count == 1)
            {
                _myCloudRootId = chain[0].Id;
            }
        }

        var lang = _settingsService.LoadSettings().UiLanguage;
        return CloudEntityRestore.WithCloudHub(
            open.Value.Entity,
            open.Value.Breadcrumb,
            ShareSheetStrings.CloudHub(lang),
            ShareSheetStrings.MyCloud(lang));
    }

    /// <summary>Folder/root ancestor after Code=name (TypeEntityId, known root, or RootKind parent).</summary>
    private async Task<bool> IsCloudTreeAncestorAsync(GetEntitiesResult node)
    {
        if (CloudEntityRestore.IsCloudFolderOrRoot(node.TypeEntityId, node.Code))
            return true;
        if (CloudEntityClassify.IsCloudRootCode(node.Code) || _knownCloudRootIds.Contains(node.Id))
            return true;
        try
        {
            var parents = await _api.GetParentEntitiesAsync(node.Id, includeValues: false)
                .ConfigureAwait(true);
            return parents.Any(p => p.Id == CloudEntityCodes.RootKindEntityId);
        }
        catch
        {
            return false;
        }
    }

    private void PersistLastOpenCloudEntityId(Guid entityId)
    {
        // Virtual hub / Shared with me ids are client-only — restoring them via GetEntities would fail.
        if (CloudVirtualNodes.IsVirtual(entityId)) return;
        try
        {
            var settings = _settingsService.LoadSettings();
            if (settings.LastOpenCloudEntityId == entityId) return;
            settings.LastOpenCloudEntityId = entityId;
            _settingsService.SaveSettings(settings);
        }
        catch
        {
            // Persistence is best-effort; navigation must not fail.
        }
    }

    private void ClearNetworkBanner()
    {
        if (!_showNetworkBanner) return;
        _showNetworkBanner = false;
        OnPropertyChanged(nameof(ShowNetworkBanner));
        OnPropertyChanged(nameof(ShowStatusBanner));
        ShareSheet.IsOnline = true;
    }

    private void SetNetworkBanner()
    {
        _lastError = null;
        OnPropertyChanged(nameof(HasErrorDetails));
        if (_showNetworkBanner) return;
        _showNetworkBanner = true;
        OnPropertyChanged(nameof(ShowNetworkBanner));
        OnPropertyChanged(nameof(ShowStatusBanner));
        ShareSheet.IsOnline = false;
    }

    private void ShowErrorDetails()
    {
        if (_lastError == null) return;
        Dialogs.ErrorDetailDialog.Show("Cloud", _lastError);
    }
}
