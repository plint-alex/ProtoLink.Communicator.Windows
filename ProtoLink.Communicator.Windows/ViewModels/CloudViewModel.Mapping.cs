using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Dialogs;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class CloudViewModel
{
    public bool IsFolderSynced(Guid folderId) => SyncMappings.Any(m => m.CloudFolderId == folderId);

    /// <summary>
    /// E6 hub / writable folder / inside a mapped tree (AT-MAP-1 + affordance when mapped) —
    /// not Shared browse-only (AT-MAP-5).
    /// </summary>
    public bool ShowAddLocalFolderCta =>
        MapLocalFolderCta.ShouldShow(
            _authService.IsAuthenticated,
            ShowSharedBrowseOnlyHint,
            IsCloudHubListing,
            CanMutateListedFolder,
            IsInsideMappedTree);

    private bool IsInsideMappedTree =>
        BreadcrumbPath.Any(b => IsFolderSynced(b.Id))
        || (CurrentEntity is { IsFolder: true } e && IsFolderSynced(e.Id));

    public string AddLocalFolderCtaLabel =>
        MapLocalFolderStrings.HubCta(_settingsService.LoadSettings().UiLanguage);

    public string AddLocalFolderAutomationName =>
        MapLocalFolderStrings.HubCtaAutomationName(_settingsService.LoadSettings().UiLanguage);

    public string MapThisFolderLabel =>
        MapLocalFolderStrings.MapThisFolder(_settingsService.LoadSettings().UiLanguage);

    public bool IsInSharedWithMeScope =>
        CloudSharedScope.IsInSharedWithMeScope(BreadcrumbPath.Select(b => b.Id));

    /// <summary>AT-S42 refined: browse-only hint only for non-writable Shared-with-me folders.</summary>
    public bool ShowSharedBrowseOnlyHint =>
        CloudSharedScope.ShowSharedBrowseOnlyHint(
            BreadcrumbPath.Select(b => b.Id),
            CurrentEntity?.Id,
            CurrentEntity is { IsFolder: true },
            _shareRootCanWrite);

    public string SharedBrowseOnlyHintText =>
        ShareSheetStrings.BrowseOnlyHint(_settingsService.LoadSettings().UiLanguage);

    public bool CanMapFolder(CloudItemViewModel item)
    {
        if (!item.IsFolder) return false;
        if (item.IsVirtualEntry) return false;
        if (IsForbiddenCloudRootMapTarget(item.Id)) return false;
        var crumbIds = BreadcrumbPath.Select(b => b.Id);
        if (!CloudSharedScope.AllowsMapUnderSharedScope(crumbIds, item.Id, _shareRootCanWrite))
            return false;
        if (IsFolderSynced(item.Id)) return false;
        return BreadcrumbPath.All(b => !IsFolderSynced(b.Id));
    }

    public bool CanMapCurrentFolder
    {
        get
        {
            if (CurrentEntity is not { IsFolder: true } entity) return false;
            if (IsForbiddenCloudRootMapTarget(entity.Id)) return false;
            var mapped = new HashSet<Guid>(SyncMappings.Select(m => m.CloudFolderId));
            return CloudSharedScope.CanMapCurrentFolder(
                entity.Id,
                BreadcrumbPath.Select(b => b.Id),
                mapped,
                _shareRootCanWrite);
        }
    }

    private bool IsForbiddenCloudRootMapTarget(Guid folderId)
    {
        _folderTreeCodes.TryGetValue(folderId, out var code);
        if (folderId == _myCloudRootId)
            code = CloudEntityCodes.CloudRoot;
        return CloudRootSelection.ShouldRefuseMapping(code, folderId, _knownCloudRootIds);
    }

    public CloudSyncMapping? CurrentFolderMapping =>
        CurrentEntity is { IsFolder: true } entity
            ? SyncMappings.FirstOrDefault(m => m.CloudFolderId == entity.Id)
            : null;

    public bool HasCurrentFolderMapping => CurrentFolderMapping != null;

    public string? CurrentFolderMappingPath => CurrentFolderMapping?.LocalPath;

    public void AddMapping(Guid cloudFolderId, string localPath, string? cloudFolderName = null)
    {
        if (string.IsNullOrWhiteSpace(localPath)) return;
        if (!Directory.Exists(localPath)) return;
        if (SyncMappings.Any(m => m.CloudFolderId == cloudFolderId)) return;
        var ancestorSynced = BreadcrumbPath.Any(b => IsFolderSynced(b.Id));
        if (ancestorSynced) return;

        _folderTreeCodes.TryGetValue(cloudFolderId, out var code);
        if (cloudFolderId == _myCloudRootId)
            code = CloudEntityCodes.CloudRoot;
        if (CloudRootSelection.ShouldRefuseMapping(code, cloudFolderId, _knownCloudRootIds))
        {
            var lang = _settingsService.LoadSettings().UiLanguage;
            var notesRoot = _settingsService.LoadSettings().NotesRootPath;
            var isNotes = CloudRootSelection.IsNotesRootPath(localPath, notesRoot);
            System.Windows.MessageBox.Show(
                isNotes
                    ? MapLocalFolderStrings.RefuseNotesRootOntoCloudRoot(lang)
                    : MapLocalFolderStrings.RefuseMapOntoCloudRoot(lang),
                MapLocalFolderStrings.RefuseMapTitle(lang),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        SyncMappings.Add(new CloudSyncMapping { CloudFolderId = cloudFolderId, LocalPath = localPath.Trim(), CloudFolderName = cloudFolderName ?? "" });
        _syncStore.Save(SyncMappings);
        StatusText = "Mapped — syncing into existing cloud folder…";
        NotifyHeaderAndInfoChanged();
        _onAfterMappedSync?.Invoke();
        _ = LoadCurrentAsync();
        // Full reconcile into the existing CloudFolderId — never create a new root folder for the map.
        _ = _mappedSync.RequestFullSyncAsync();
    }

    /// <summary>
    /// E6 local-first wizard: pick local → create or pick Cloud folder → <see cref="AddMapping"/> + full reconcile.
    /// Candidate listing soft-fails (first page only) so a bloated Drive-style CloudRoot cannot block create-then-map.
    /// </summary>
    public async Task RunAddLocalFolderWizardAsync(string? prefilledLocalPath = null)
    {
        if (!_authService.IsAuthenticated)
        {
            _onAuthRequired?.Invoke();
            return;
        }

        var lang = _settingsService.LoadSettings().UiLanguage;
        try
        {
            // Resolve My Cloud id without navigating to hub (FolderPicker must open promptly).
            await EnsureWizardCloudRootAsync().ConfigureAwait(true);
            var parentId = ResolveWizardParentFolderId();
            if (parentId == Guid.Empty)
            {
                System.Windows.MessageBox.Show(
                    MapLocalFolderStrings.WizardParentMissing(lang),
                    MapLocalFolderStrings.WizardTitle(lang),
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            var candidates = await LoadUnmappedChildFoldersAsync(parentId).ConfigureAwait(true);
            var choice = MapLocalFolderWizardDialog.TryShow(lang, prefilledLocalPath, candidates);
            if (choice is null) return;

            Guid folderId;
            string folderName;
            if (choice.CreateNew)
            {
                var name = choice.NewFolderName!.Trim();
                folderId = await _api.AddEntityAsync(CloudEntityCodes.CloudFolder, new[] { parentId }, name)
                    .ConfigureAwait(true);
                folderName = name;
            }
            else
            {
                folderId = choice.ExistingFolderId!.Value;
                folderName = choice.ExistingFolderName ?? "";
            }

            AddMapping(folderId, choice.LocalPath, folderName);
            await OpenAsync(
                new CloudEntityRef(folderId, CloudEntityKind.Folder, folderName, parentId),
                BuildBreadcrumbForMappedFolder(parentId, folderId, folderName),
                pushHistory: true).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ReportCaughtException(ex);
        }
    }

    /// <summary>Ensure <see cref="_myCloudRootId"/> without opening the hub (wizard must not stall on navigation).</summary>
    private async Task EnsureWizardCloudRootAsync()
    {
        if (_authService.CurrentToken == null) return;
        var userId = _authService.CurrentToken.UserId;
        var list = await _api.GetEntitiesAsync(new[] { userId }, includeValues: false).ConfigureAwait(true);
        var roots = (list ?? new List<GetEntitiesResult>())
            .Where(e => e.Code == CloudEntityCodes.CloudRoot)
            .Select(e => new CloudRootSelection.Candidate(e.Id, e.CreationTime))
            .ToList();

        _knownCloudRootIds.Clear();
        foreach (var r in roots)
            _knownCloudRootIds.Add(r.Id);

        var mapped = new HashSet<Guid>(SyncMappings.Select(m => m.CloudFolderId));
        // Create-then-map prefers newest non-mapped root (not LastOpen Drive).
        var wizardRoot = CloudRootSelection.PickWizardCreateParentRoot(roots, mapped);
        if (wizardRoot is { } id && id != Guid.Empty)
        {
            _myCloudRootId = id;
            _folderTreeCodes[id] = CloudEntityCodes.CloudRoot;
            return;
        }

        await ApplyCanonicalCloudRootFromUserChildrenAsync(list, userId).ConfigureAwait(true);
    }

    private Guid ResolveWizardParentFolderId()
    {
        // Hub / CloudRoot / inside a mapping → create under My Cloud (never map onto CloudRoot).
        if (IsCloudHubListing
            || IsInsideMappedTree
            || (ListedFolderId is { } listedRoot && IsForbiddenCloudRootMapTarget(listedRoot)))
            return _myCloudRootId;

        if (CanMutateListedFolder && ListedFolderId is { } listed)
            return listed;
        return _myCloudRootId;
    }

    private IReadOnlyList<CloudBreadcrumbEntry> BuildBreadcrumbForMappedFolder(
        Guid parentId,
        Guid folderId,
        string folderName)
    {
        var lang = _settingsService.LoadSettings().UiLanguage;
        var crumbs = new List<CloudBreadcrumbEntry>
        {
            new() { Id = CloudVirtualNodes.HubId, Name = ShareSheetStrings.CloudHub(lang) }
        };
        if (parentId != Guid.Empty && parentId != folderId)
        {
            crumbs.Add(new CloudBreadcrumbEntry
            {
                Id = parentId,
                Name = parentId == _myCloudRootId
                    ? ShareSheetStrings.MyCloud(lang)
                    : "Cloud"
            });
        }

        crumbs.Add(new CloudBreadcrumbEntry { Id = folderId, Name = folderName });
        return crumbs;
    }

    /// <summary>
    /// First page only + soft-fail: bloated CloudRoot must not block the create-new path (AT-MAP-2).
    /// </summary>
    private async Task<IReadOnlyList<MapLocalFolderCandidate>> LoadUnmappedChildFoldersAsync(Guid parentId)
    {
        try
        {
            var list = await _api.GetEntitiesAsync(
                    new[] { parentId },
                    includeValues: true,
                    maxPages: CloudApiService.InteractiveListingMaxPages)
                .ConfigureAwait(true);
            var mapped = new HashSet<Guid>(SyncMappings.Select(m => m.CloudFolderId));
            var result = new List<MapLocalFolderCandidate>();
            foreach (var e in list ?? Enumerable.Empty<GetEntitiesResult>())
            {
                if (e.Code != CloudEntityCodes.CloudFolder) continue;
                if (mapped.Contains(e.Id)) continue;
                if (IsFolderSynced(e.Id)) continue;
                result.Add(new MapLocalFolderCandidate
                {
                    Id = e.Id,
                    Name = CloudApiService.GetDisplayName(e)
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            // Create-new still works with an empty pick list; do not abort the wizard.
            System.Diagnostics.Debug.WriteLine(
                $"LoadUnmappedChildFolders soft-fail: {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<MapLocalFolderCandidate>();
        }
    }

    public void RemoveMapping(Guid cloudFolderId)
    {
        var mapping = SyncMappings.FirstOrDefault(m => m.CloudFolderId == cloudFolderId);
        if (mapping == null) return;
        SyncMappings.Remove(mapping);
        _syncStore.Save(SyncMappings);
        NotifyHeaderAndInfoChanged();
        _onAfterMappedSync?.Invoke();
        _ = LoadCurrentAsync();
    }

    public Task SyncMappingAsync(CloudSyncMapping mapping)
    {
        _mappedSync.StartManualSync(mapping);
        return Task.CompletedTask;
    }

    public Task ForceUploadMappingAsync(CloudSyncMapping mapping)
    {
        var confirm = System.Windows.MessageBox.Show(
            $"Upload all local files in:\n{mapping.LocalPath}\n\nto the server (overwrite remote)?",
            "Force upload",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes)
            return Task.CompletedTask;
        _mappedSync.StartForcePush(mapping);
        return Task.CompletedTask;
    }

    public Task ForceDownloadMappingAsync(CloudSyncMapping mapping)
    {
        var confirm = System.Windows.MessageBox.Show(
            $"Download all server files for “{mapping.CloudFolderName}”\n\noverwriting local files in:\n{mapping.LocalPath}?",
            "Force download",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes)
            return Task.CompletedTask;
        _mappedSync.StartForcePull(mapping);
        return Task.CompletedTask;
    }

    public async Task SyncAllMappingsOnStartupAsync()
    {
        if (!_authService.IsAuthenticated)
        {
            StatusText = "Please log in.";
            return;
        }
        var mappings = SyncMappings
            .Where(m => !string.IsNullOrWhiteSpace(m.LocalPath) && Directory.Exists(m.LocalPath))
            .ToList();
        if (mappings.Count == 0) return;
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => StatusText = "Syncing…");
        await _mappedSync.SynchronizeAllMappedAsync();
    }

    private void SyncCurrentMapping()
    {
        if (CurrentFolderMapping is { } mapping)
            _ = SyncMappingAsync(mapping);
    }

    private void ForceUploadCurrentMapping()
    {
        if (CurrentFolderMapping is { } mapping)
            _ = ForceUploadMappingAsync(mapping);
    }

    private void ForceDownloadCurrentMapping()
    {
        if (CurrentFolderMapping is { } mapping)
            _ = ForceDownloadMappingAsync(mapping);
    }

    private void UnmapCurrentFolder()
    {
        if (CurrentEntity is { IsFolder: true } entity)
            RemoveMapping(entity.Id);
    }
}
