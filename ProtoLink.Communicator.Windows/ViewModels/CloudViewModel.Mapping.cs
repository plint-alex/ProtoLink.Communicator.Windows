using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class CloudViewModel
{
    public bool IsFolderSynced(Guid folderId) => SyncMappings.Any(m => m.CloudFolderId == folderId);

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
            var mapped = new HashSet<Guid>(SyncMappings.Select(m => m.CloudFolderId));
            return CloudSharedScope.CanMapCurrentFolder(
                entity.Id,
                BreadcrumbPath.Select(b => b.Id),
                mapped,
                _shareRootCanWrite);
        }
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
        if (SyncMappings.Any(m => m.CloudFolderId == cloudFolderId)) return;
        var ancestorSynced = BreadcrumbPath.Any(b => IsFolderSynced(b.Id));
        if (ancestorSynced) return;
        SyncMappings.Add(new CloudSyncMapping { CloudFolderId = cloudFolderId, LocalPath = localPath.Trim(), CloudFolderName = cloudFolderName ?? "" });
        _syncStore.Save(SyncMappings);
        StatusText = "Mapped — syncing into existing cloud folder…";
        NotifyHeaderAndInfoChanged();
        _ = LoadCurrentAsync();
        // Full reconcile into the existing CloudFolderId — never create a new root folder for the map.
        _ = _mappedSync.RequestFullSyncAsync();
    }

    public void RemoveMapping(Guid cloudFolderId)
    {
        var mapping = SyncMappings.FirstOrDefault(m => m.CloudFolderId == cloudFolderId);
        if (mapping == null) return;
        SyncMappings.Remove(mapping);
        _syncStore.Save(SyncMappings);
        NotifyHeaderAndInfoChanged();
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
