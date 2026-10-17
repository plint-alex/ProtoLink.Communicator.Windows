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
            var localMirror = CloudMappingMirrorPaths.GetLocalPathForCloudItem(item, BreadcrumbPath, SyncMappings);
            _mappedSync.SuspendMappedRootWatchers();
            try
            {
                if (item.IsFolder)
                {
                    var children = await _api.GetEntitiesAsync(new[] { item.Id }, includeValues: false);
                    foreach (var c in children ?? new List<GetEntitiesResult>())
                    {
                        if (c.Code == CloudEntityCodes.CloudFile)
                            await _api.DeleteEntityAsync(c.Id);
                        else if (c.Code == CloudEntityCodes.CloudFolder)
                            await DeleteFolderRecursiveAsync(c.Id);
                    }
                    await _api.DeleteEntityAsync(item.Id);
                }
                else
                {
                    await _api.DeleteEntityAsync(item.Id);
                }

                CloudMappingMirrorPaths.DeleteLocalMirror(item, localMirror);
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

    private async Task DeleteFolderRecursiveAsync(Guid folderId)
    {
        var children = await _api.GetEntitiesAsync(new[] { folderId }, includeValues: false);
        foreach (var c in children ?? new List<GetEntitiesResult>())
        {
            if (c.Code == CloudEntityCodes.CloudFile)
                await _api.DeleteEntityAsync(c.Id);
            else
                await DeleteFolderRecursiveAsync(c.Id);
        }
        await _api.DeleteEntityAsync(folderId);
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
