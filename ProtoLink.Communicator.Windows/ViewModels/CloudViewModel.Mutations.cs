using System.IO;
using System.Linq;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class CloudViewModel
{
    private void NewSubfolder(CloudItemViewModel parentFolder)
    {
        if (!parentFolder.IsFolder) return;
        if (!Dialogs.InputDialog.TryShow("Folder name:", "New subfolder", out var name) || string.IsNullOrWhiteSpace(name)) return;
        var nameTrim = name.Trim();
        var currentId = ListedFolderId!.Value;
        var parentName = parentFolder.Name;
        var breadcrumbSnapshot = BreadcrumbPath.ToList();
        var mappingsSnapshot = SyncMappings.ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                if (CloudMappingMirrorPaths.TryGetSyncedMirrorForChildFolder(
                        currentId, parentName, breadcrumbSnapshot, mappingsSnapshot, out var root, out var parentLocal))
                {
                    Directory.CreateDirectory(Path.Combine(parentLocal, nameTrim));
                    await _mappedSync.RunFolderSynchronizeAsync(root.CloudFolderId, root.LocalPath);
                }
                else
                {
                    await _api.AddEntityAsync(CloudEntityCodes.CloudFolder, new[] { parentFolder.Id }, nameTrim);
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => LoadCurrentAsync(quiet: true));
            }
            catch (Exception ex)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => ReportCaughtException(ex));
            }
        });
    }

    private void UploadHere(CloudItemViewModel targetFolder)
    {
        if (!targetFolder.IsFolder) return;
        var dlg = new Microsoft.Win32.OpenFileDialog();
        if (dlg.ShowDialog() != true) return;
        var path = dlg.FileName;
        var currentId = ListedFolderId!.Value;
        var targetName = targetFolder.Name;
        var breadcrumbSnapshot = BreadcrumbPath.ToList();
        var mappingsSnapshot = SyncMappings.ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                var fileName = Path.GetFileName(path);
                if (CloudMappingMirrorPaths.TryGetSyncedMirrorForChildFolder(
                        currentId, targetName, breadcrumbSnapshot, mappingsSnapshot, out var root, out var targetLocal))
                {
                    Directory.CreateDirectory(targetLocal);
                    File.Copy(path, Path.Combine(targetLocal, fileName), overwrite: true);
                    await _mappedSync.RunFolderSynchronizeAsync(root.CloudFolderId, root.LocalPath);
                }
                else
                {
                    await UploadNewCloudFileAsync(targetFolder.Id, path);
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => LoadCurrentAsync(quiet: true));
            }
            catch (Exception ex)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => ReportCaughtException(ex));
            }
        });
    }

    private void NewFolder()
    {
        if (ListedFolderId is not { } currentId) return;
        if (!Dialogs.InputDialog.TryShow("Folder name:", "New folder", out var name) || string.IsNullOrWhiteSpace(name)) return;
        var nameTrim = name.Trim();
        var breadcrumbSnapshot = BreadcrumbPath.ToList();
        var mappingsSnapshot = SyncMappings.ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                if (CloudMappingMirrorPaths.TryGetSyncedMirror(currentId, breadcrumbSnapshot, mappingsSnapshot, out var root, out var localDir))
                {
                    Directory.CreateDirectory(Path.Combine(localDir, nameTrim));
                    await _mappedSync.RunFolderSynchronizeAsync(root.CloudFolderId, root.LocalPath);
                }
                else
                {
                    await _api.AddEntityAsync(CloudEntityCodes.CloudFolder, new[] { currentId }, nameTrim);
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => LoadCurrentAsync(quiet: true));
            }
            catch (Exception ex)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => ReportCaughtException(ex));
            }
        });
    }

    private void Upload()
    {
        if (ListedFolderId is not { } currentId) return;
        var dlg = new Microsoft.Win32.OpenFileDialog();
        if (dlg.ShowDialog() != true) return;
        var path = dlg.FileName;
        var breadcrumbSnapshot = BreadcrumbPath.ToList();
        var mappingsSnapshot = SyncMappings.ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                var fileName = Path.GetFileName(path);
                if (CloudMappingMirrorPaths.TryGetSyncedMirror(currentId, breadcrumbSnapshot, mappingsSnapshot, out var root, out var localDir))
                {
                    Directory.CreateDirectory(localDir);
                    File.Copy(path, Path.Combine(localDir, fileName), overwrite: true);
                    await _mappedSync.RunFolderSynchronizeAsync(root.CloudFolderId, root.LocalPath);
                }
                else
                {
                    await UploadNewCloudFileAsync(currentId, path);
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => LoadCurrentAsync(quiet: true));
            }
            catch (Exception ex)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => ReportCaughtException(ex));
            }
        });
    }

    private async Task UploadNewCloudFileAsync(Guid parentFolderId, string localFilePath)
    {
        var fileName = Path.GetFileName(localFilePath);
        var entityId = await _api.AddEntityAsync(CloudEntityCodes.CloudFile, new[] { parentFolderId }, fileName);
        await using var stream = File.OpenRead(localFilePath);
        var mime = MimeTypes.GetMimeType(fileName);
        await _api.AddFileAsync(entityId, fileName, stream, mime);
    }

    private async Task DeleteAsync(CloudItemViewModel item) =>
        await DeleteCloudTreeAsync(item, forAllShared: false);

    /// <summary>E12 For all users — revoke grants + cascade delete (Api DeleteSharedForAll).</summary>
    public async Task DeleteSharedForAllAsync(CloudItemViewModel item) =>
        await DeleteCloudTreeAsync(item, forAllShared: true);

    /// <summary>E12 For me only — Leave (SWM) or DetachFromMyCloud (owner outbound).</summary>
    public async Task DeleteSharedForMeOnlyAsync(CloudItemViewModel item)
    {
        try
        {
            if (item.IsVirtualEntry || CloudVirtualNodes.IsVirtual(item.Id)) return;
            var lang = _settingsService.LoadSettings().UiLanguage;
            var crumbIds = BreadcrumbPath.Select(b => b.Id).ToList();
            var inSwmScope = CloudSharedScope.IsInSharedWithMeScope(crumbIds) || IsSharedWithMeListing;
            var outbound = _outboundShareRootIds.Contains(item.Id);
            // AT-SFD-4: creator LeaveSharedWithMe is Left=false / MainParent no-op. Only Leave
            // when clearly inbound SWM; known outbound or My Cloud listing → DetachFromMyCloud.
            if (!CloudSharedDelete.PreferDetachForMeOnly(
                    IsSwmShareRootItem(item),
                    inSwmScope,
                    outbound))
            {
                await LeaveSharedAsync(item.Id);
                return;
            }

            StatusText = "Removing…";
            // PreferDetach tip 1.0.137 still left MainParent on CloudRoot — verify + one retry.
            if (!await TryDetachFromMyCloudVerifiedAsync(item.Id))
            {
                StatusText = CloudSharedDeleteStrings.DetachFailed(lang, item.Name);
                return;
            }

            _outboundShareRootIds.Remove(item.Id);
            _shareRootCaptions.Remove(item.Id);
            _shareRootCanWrite.Remove(item.Id);
            // Soft Ready / disk listing must not keep painting the detached id under My Cloud.
            _listingMetaStore.RemoveByRemoteIds(new[] { item.Id });
            _listingDiskCache.RemoveRemoteIds(new[] { item.Id });
            _deletedRemoteIds.Add(item.Id);
            PurgeDeletedFromFolderTree(new[] { item.Id });
            Items.Remove(item);
            _locations.Remove(item.Id);

            var statusOk = CloudSharedDeleteStrings.DetachedStatus(lang, item.Name);
            // When Delete ran from inside the folder (More on open entity), parent crumb is My Cloud.
            var viewingDetached =
                CurrentEntity?.Id == item.Id
                || ListedFolderId == item.Id
                || crumbIds.Contains(item.Id);
            if (viewingDetached)
            {
                var parent = crumbIds.LastOrDefault(id => id != item.Id && !CloudVirtualNodes.IsVirtual(id));
                if (parent == Guid.Empty && _myCloudRootId != Guid.Empty)
                    parent = _myCloudRootId;
                if (parent != Guid.Empty
                    && _locations.TryGetValue(parent, out var parentLoc))
                {
                    StatusText = statusOk;
                    await OpenAsync(parentLoc.Entity, parentLoc.Breadcrumb.ToList(), pushHistory: false);
                    return;
                }

                if (_myCloudRootId != Guid.Empty)
                {
                    StatusText = statusOk;
                    await EnsureRootAsync();
                    return;
                }
            }

            StatusText = statusOk;
            await LoadCurrentAsync();
        }
        catch (Exception ex)
        {
            ReportCaughtException(ex);
        }
    }

    /// <summary>
    /// AT-SFD-4: Detach must self-root MainParentId. Api can claim detached=true while
    /// GetEntities still shows CloudRoot — verify via ids listing and retry once.
    /// </summary>
    private async Task<bool> TryDetachFromMyCloudVerifiedAsync(Guid entityId)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await _api.DetachFromMyCloudAsync(entityId);
            if (!result.Detached)
                continue;

            var rows = await _api.GetEntitiesByIdsAsync(new[] { entityId }, includeValues: false);
            var mp = rows.FirstOrDefault(r => r.Id == entityId)?.MainParentId;
            if (mp == entityId)
                return true;

            // Card fallback when listing omits mainParentId (older Api).
            var card = await _api.GetEntityCardAsync(entityId, fromCache: false);
            if (card?.MainParentId == entityId)
                return true;

            // Detached + MainParentCleared with no readable MainParent — trust Api claim.
            if (result.MainParentCleared && mp is null && card?.MainParentId is null)
                return true;
        }

        return false;
    }

    private async Task DeleteCloudTreeAsync(CloudItemViewModel item, bool forAllShared)
    {
        try
        {
            if (!CanDeleteItem(item))
            {
                StatusText = "You don't have permission to delete this item.";
                return;
            }

            StatusText = "Deleting…";
            // Disk may have dedicated maps written after start (AT-CFD inject / heal) — merge before mirror resolve.
            CloudSyncMappingsMerge.UnionFromStore(SyncMappings, _syncStore.Load());
            var localMirror = CloudMappingMirrorPaths.GetLocalPathForCloudItem(item, BreadcrumbPath, SyncMappings);
            var wasMappedRoot = item.IsFolder && IsFolderSynced(item.Id);
            // Known local subtree ids (UI tree / Soft Ready meta / listing) — purge after
            // single-root delete; do not N+1 GetEntities+DeleteEntity (Api cascade).
            var deletedIds = new HashSet<Guid>();
            if (item.IsFolder)
                CollectKnownLocalSubtreeIds(item.Id, deletedIds);
            else
                deletedIds.Add(item.Id);

            _mappedSync.SuspendMappedRootWatchers();
            try
            {
                // Release Sync file handles under parent maps before Directory.Delete (AT-CFD-6).
                await _mappedSync.CancelAndWaitIdleAsync(TimeSpan.FromSeconds(8));

                // Fail-closed: if this throws, do not pretend gone / purge as success.
                if (forAllShared)
                {
                    await _api.DeleteSharedForAllAsync(item.Id);
                    deletedIds.Add(item.Id);
                }
                else
                {
                    await CloudFolderDelete.DeleteSingleRootAsync(
                        item.Id,
                        id => _api.DeleteEntityAsync(id),
                        deletedIds);
                }

                _outboundShareRootIds.Remove(item.Id);
                _shareRootCaptions.Remove(item.Id);
                _shareRootCanWrite.Remove(item.Id);

                // Soft Ready paints sync-meta children when GetEntities times out — purge so
                // deleted ids cannot resurrect after a successful Cloud soft-hide (E10 regress).
                _listingMetaStore.RemoveByRemoteIds(deletedIds);
                _listingDiskCache.RemoveRemoteIds(deletedIds);
                foreach (var id in deletedIds)
                    _deletedRemoteIds.Add(id);
                PurgeDeletedFromFolderTree(deletedIds);

                if (!string.IsNullOrWhiteSpace(localMirror))
                    PurgeCoveringParentMetaForMirror(localMirror);

                // NFR-SYNC-1: mirror IO soft-fails; Cloud tree already deleted.
                await MappedFolderIoGate.RunSyncAsync(() =>
                    CloudMappingMirrorPaths.DeleteLocalMirror(item, localMirror));

                if (!string.IsNullOrWhiteSpace(localMirror))
                    Services.Sync.DeletedLocalMirrorTombstones.Remember(localMirror);

                if (wasMappedRoot)
                    RemoveMappingQuiet(item.Id);

                Items.Remove(item);
                _locations.Remove(item.Id);
                if (CurrentEntity?.Id == item.Id && ListedFolderId is { } listedId && _locations.TryGetValue(listedId, out var folder))
                    CurrentEntity = folder.Entity;
            }
            finally
            {
                _mappedSync.ResumeMappedRootWatchers();
            }

            await LoadCurrentAsync();
        }
        catch (Exception ex)
        {
            ReportCaughtException(ex);
        }
    }

    /// <summary>
    /// Gather folder/file ids under <paramref name="rootId"/> from local UI tree cache,
    /// Soft Ready sync-meta, and the current listing — no GetEntities. Used to purge after
    /// single-root DeleteEntity (Api cascade owns the remote subtree).
    /// </summary>
    private void CollectKnownLocalSubtreeIds(Guid rootId, HashSet<Guid> into)
    {
        CloudFolderDelete.CollectKnownSubtreeIds(
            rootId,
            id =>
            {
                var list = new List<CloudFolderDelete.ChildRef>();
                if (_folderTreeChildren.TryGetValue(id, out var nodes) && nodes != null)
                {
                    foreach (var n in nodes)
                        list.Add(new CloudFolderDelete.ChildRef(
                            n.Id, CloudEntityCodes.CloudFolder, CloudEntityCodes.TypeCloudFolderId));
                }

                foreach (var m in _listingMetaStore.FindChildrenByParentRemoteId(id))
                {
                    if (m.RemoteId == Guid.Empty) continue;
                    list.Add(new CloudFolderDelete.ChildRef(
                        m.RemoteId,
                        m.IsFolder ? CloudEntityCodes.CloudFolder : CloudEntityCodes.CloudFile,
                        m.IsFolder ? CloudEntityCodes.TypeCloudFolderId : CloudEntityCodes.TypeCloudFileId));
                }

                if (ListedFolderId == id)
                {
                    foreach (var row in Items)
                    {
                        list.Add(new CloudFolderDelete.ChildRef(
                            row.Id,
                            row.IsFolder ? CloudEntityCodes.CloudFolder : CloudEntityCodes.CloudFile,
                            row.IsFolder ? CloudEntityCodes.TypeCloudFolderId : CloudEntityCodes.TypeCloudFileId));
                    }
                }

                return list;
            },
            into);
    }

    /// <summary>
    /// When a dedicated map path sits under another mapping (MyNotesFolder), drop that relative
    /// prefix from sync-meta so Soft Ready / Compare cannot keep the deleted tree "alive".
    /// </summary>
    private void PurgeCoveringParentMetaForMirror(string localMirror)
    {
        string fullMirror;
        try { fullMirror = Path.GetFullPath(localMirror); }
        catch { fullMirror = localMirror; }

        foreach (var map in SyncMappings.ToList())
        {
            if (string.IsNullOrWhiteSpace(map.LocalPath)) continue;
            string fullRoot;
            try { fullRoot = Path.GetFullPath(map.LocalPath); }
            catch { continue; }

            if (string.Equals(fullRoot, fullMirror, StringComparison.OrdinalIgnoreCase))
            {
                _listingMetaStore.ClearMapping(map.CloudFolderId.ToString("N"));
                continue;
            }

            var rootPrefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
            if (!fullMirror.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var rel = Path.GetRelativePath(fullRoot, fullMirror).Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(rel) || rel.StartsWith("..", StringComparison.Ordinal))
                continue;

            var twinIds = _listingMetaStore.RemoveByRelativePathPrefix(map.CloudFolderId.ToString("N"), rel);
            foreach (var id in twinIds)
                _deletedRemoteIds.Add(id);
        }
    }

    public async Task DownloadAsync(CloudItemViewModel item)
    {
        if (item.IsFolder) return;
        try
        {
            var stream = await _api.GetFileStreamAsync(item.Id);
            DownloadFile?.Invoke(item.Name, stream);
        }
        catch (Exception ex)
        {
            ReportCaughtException(ex);
        }
    }

    private async Task DownloadEntityAsync(CloudEntityRef file)
    {
        try
        {
            var stream = await _api.GetFileStreamAsync(file.Id);
            DownloadFile?.Invoke(file.Name, stream);
        }
        catch (Exception ex)
        {
            ReportCaughtException(ex);
        }
    }
}
