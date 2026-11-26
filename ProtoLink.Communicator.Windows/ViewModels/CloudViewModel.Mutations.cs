using System.IO;
using System.Linq;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
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

    private async Task DeleteAsync(CloudItemViewModel item)
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
            CloudSyncMappingsMerge.AddMissing(SyncMappings, _syncStore.Load());
            var localMirror = CloudMappingMirrorPaths.GetLocalPathForCloudItem(item, BreadcrumbPath, SyncMappings);
            var wasMappedRoot = item.IsFolder && IsFolderSynced(item.Id);
            var deletedIds = new HashSet<Guid>();
            _mappedSync.SuspendMappedRootWatchers();
            try
            {
                if (item.IsFolder)
                {
                    // Leaf-first recursive DeleteEntity (defense-in-depth vs Api cascade).
                    // Fail-closed: GetEntities throw/null aborts — never soft-hide parent alone.
                    await CloudFolderDelete.DeleteFolderRecursiveAsync(
                        item.Id,
                        async id =>
                        {
                            var kids = await _api.GetEntitiesAsync(new[] { id }, includeValues: false);
                            if (kids == null)
                            {
                                throw new InvalidOperationException(
                                    $"Cloud folder delete listing failed under {id:D}; aborting to avoid promote.");
                            }
                            return kids
                                .Select(c => new CloudFolderDelete.ChildRef(c.Id, c.Code, c.TypeEntityId))
                                .ToList();
                        },
                        id => _api.DeleteEntityAsync(id),
                        deletedIds);
                }
                else
                {
                    await _api.DeleteEntityAsync(item.Id);
                    deletedIds.Add(item.Id);
                }

                // Soft Ready paints sync-meta children when GetEntities times out — purge so
                // deleted ids cannot resurrect after a successful Cloud soft-hide (E10 regress).
                // Shared store with SyncEngine (ctor) so AutoPush cannot rewrite purged rows.
                _listingMetaStore.RemoveByRemoteIds(deletedIds);
                _listingDiskCache.RemoveRemoteIds(deletedIds);
                foreach (var id in deletedIds)
                    _deletedRemoteIds.Add(id);
                PurgeDeletedFromFolderTree(deletedIds);

                // NFR-SYNC-1: mirror IO soft-fails; Cloud tree already deleted.
                CloudMappingMirrorPaths.DeleteLocalMirror(item, localMirror);
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
