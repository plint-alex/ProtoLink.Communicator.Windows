using System.IO;
using System.Linq;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Services;

public class CloudFolderSynchronizer
{
    private readonly CloudApiService _api;

    public CloudFolderSynchronizer(CloudApiService api)
    {
        _api = api;
    }

    public async Task<bool> SynchronizeAsync(
        Guid cloudFolderId,
        string localPath,
        Action<string> reportStatus,
        Action<Exception>? reportFullError,
        string? mappedLocalRootPath = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(localPath))
        {
            reportStatus("Local path does not exist.");
            return false;
        }

        try
        {
            reportStatus("Syncing…");
            await SynchronizeFolderAsync(
                cloudFolderId,
                localPath,
                mappedLocalRootPath,
                cancellationToken);
            reportStatus("Sync complete.");
            return true;
        }
        catch (Exception ex)
        {
            reportStatus("Error: " + ex.Message);
            reportFullError?.Invoke(ex);
            return false;
        }
    }

    private async Task SynchronizeFolderAsync(
        Guid cloudFolderId,
        string localPath,
        string? mappedLocalRootPath,
        CancellationToken cancellationToken)
    {
        var list = await _api.GetEntitiesAsync(new[] { cloudFolderId }, includeValues: true);
        var cloudFolders = list.Where(e => e.Code == CloudEntityCodes.CloudFolder).ToList();
        var cloudFiles = list.Where(e => e.Code == CloudEntityCodes.CloudFile).ToList();

        var localFiles = Directory.GetFiles(localPath);
        var localDirNames = new HashSet<string>(
            Directory.GetDirectories(localPath).Select(Path.GetFileName).Where(n => n != null).Cast<string>(),
            StringComparer.OrdinalIgnoreCase);

        var cloudFolderNames = new Dictionary<string, GetEntitiesResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in cloudFolders)
        {
            var name = CloudApiService.GetDisplayName(e);
            if (!string.IsNullOrEmpty(name)) cloudFolderNames[name] = e;
        }

        var cloudFileByName = new Dictionary<string, GetEntitiesResult>(StringComparer.OrdinalIgnoreCase);
        var syntheticCloudFiles = new List<GetEntitiesResult>();
        foreach (var e in cloudFiles)
        {
            var disp = CloudApiService.GetDisplayName(e);
            if (string.IsNullOrEmpty(disp)) continue;
            if (CloudApiService.IsSyntheticCloudFileDisplayName(e, disp))
                syntheticCloudFiles.Add(e);
            else
                cloudFileByName[disp] = e;
        }

        var emptySyntheticReuseQueue = new Queue<Guid>();
        var syntheticRemoteBySize = new List<(Guid Id, long Length)>();
        foreach (var e in syntheticCloudFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remoteStream = await _api.GetFileStreamOrNotFoundAsync(e.Id, cancellationToken);
            if (remoteStream == null)
                emptySyntheticReuseQueue.Enqueue(e.Id);
            else
            {
                try
                {
                    syntheticRemoteBySize.Add((e.Id, remoteStream.Length));
                }
                finally
                {
                    await remoteStream.DisposeAsync();
                }
            }
        }

        foreach (var kv in cloudFolderNames)
        {
            var name = kv.Key;
            if (localDirNames.Contains(name)) continue;
            cancellationToken.ThrowIfCancellationRequested();
            var subPath = Path.Combine(localPath, name);
            Directory.CreateDirectory(subPath);
            localDirNames.Add(name);
        }

        foreach (var dir in Directory.GetDirectories(localPath))
        {
            var name = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(name)) continue;
            if (cloudFolderNames.ContainsKey(name)) continue;
            cancellationToken.ThrowIfCancellationRequested();
            var cloudChildId = await _api.AddEntityAsync(CloudEntityCodes.CloudFolder, new[] { cloudFolderId }, name);
            cloudFolderNames[name] = new GetEntitiesResult { Id = cloudChildId, Code = CloudEntityCodes.CloudFolder };
        }

        foreach (var dir in Directory.GetDirectories(localPath))
        {
            var name = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(name) || !cloudFolderNames.TryGetValue(name, out var cloudEnt)) continue;
            var subPath = Path.Combine(localPath, name);
            await SynchronizeFolderAsync(
                cloudEnt.Id,
                subPath,
                mappedLocalRootPath,
                cancellationToken);
        }

        foreach (var kv in cloudFileByName)
        {
            var name = kv.Key;
            var entity = kv.Value;
            var localFilePath = Path.Combine(localPath, name);
            var localExists = File.Exists(localFilePath);

            var remoteStream = await _api.GetFileStreamOrNotFoundAsync(entity.Id, cancellationToken);
            try
            {
                if (!localExists)
                {
                    if (remoteStream != null)
                    {
                        await using (var fs = File.Create(localFilePath))
                            await remoteStream.CopyToAsync(fs, cancellationToken);
                    }

                    continue;
                }

                if (remoteStream == null)
                {
                    await using var content = File.OpenRead(localFilePath);
                    var mime = MimeTypes.GetMimeType(name);
                    await _api.AddFileAsync(entity.Id, name, content, mime);
                    continue;
                }

                var localLen = new FileInfo(localFilePath).Length;
                if (localLen == remoteStream.Length)
                    continue;

                await using var uploadContent = File.OpenRead(localFilePath);
                var mimeType = MimeTypes.GetMimeType(name);
                await _api.AddFileAsync(entity.Id, name, uploadContent, mimeType);
            }
            finally
            {
                if (remoteStream != null)
                    await remoteStream.DisposeAsync();
            }
        }

        foreach (var f in localFiles)
        {
            var name = Path.GetFileName(f);
            if (string.IsNullOrEmpty(name)) continue;
            if (cloudFileByName.ContainsKey(name)) continue;

            if (emptySyntheticReuseQueue.Count > 0)
            {
                var reuseId = emptySyntheticReuseQueue.Dequeue();
                await _api.AddValueAsync(reuseId, name);
                await using var content = File.OpenRead(f);
                var mime = MimeTypes.GetMimeType(name);
                await _api.AddFileAsync(reuseId, name, content, mime);
                cloudFileByName[name] = new GetEntitiesResult { Id = reuseId, Code = CloudEntityCodes.CloudFile };
                continue;
            }

            var localLen = new FileInfo(f).Length;
            var sizeMatchIdx = -1;
            var sizeMatchCount = 0;
            for (var i = 0; i < syntheticRemoteBySize.Count; i++)
            {
                if (syntheticRemoteBySize[i].Length != localLen) continue;
                sizeMatchCount++;
                sizeMatchIdx = i;
            }

            if (sizeMatchCount == 1 && sizeMatchIdx >= 0)
            {
                var matchedId = syntheticRemoteBySize[sizeMatchIdx].Id;
                syntheticRemoteBySize.RemoveAt(sizeMatchIdx);
                await _api.AddValueAsync(matchedId, name);
                cloudFileByName[name] = new GetEntitiesResult { Id = matchedId, Code = CloudEntityCodes.CloudFile };
                continue;
            }

            var entityId = await _api.AddEntityAsync(CloudEntityCodes.CloudFile, new[] { cloudFolderId }, name);
            await using var upload = File.OpenRead(f);
            var mimeType = MimeTypes.GetMimeType(name);
            await _api.AddFileAsync(entityId, name, upload, mimeType);
            cloudFileByName[name] = new GetEntitiesResult { Id = entityId, Code = CloudEntityCodes.CloudFile };
        }

        while (emptySyntheticReuseQueue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _api.DeleteEntityAsync(emptySyntheticReuseQueue.Dequeue());
        }
    }
}
