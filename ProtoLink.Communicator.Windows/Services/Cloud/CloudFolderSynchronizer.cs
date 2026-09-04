using System.IO;
using System.Linq;
using System.Net.Http;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services.Sync;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>Adapter: runs shared SyncEngine for one or all mapped roots.</summary>
public class CloudFolderSynchronizer
{
    private readonly CloudApiService _api;
    private readonly JsonSyncMetadataStore _store;
    private readonly SyncEngine _engine;
    private readonly Func<bool> _compareSizeAndTimeOnSync;

    public CloudFolderSynchronizer(
        CloudApiService api,
        JsonSyncMetadataStore? store = null,
        Func<bool>? compareSizeAndTimeOnSync = null)
    {
        _api = api;
        _store = store ?? new JsonSyncMetadataStore();
        _engine = new SyncEngine(_store, _api);
        _compareSizeAndTimeOnSync = compareSizeAndTimeOnSync ?? (() => true);
    }

    public async Task<bool> SynchronizeAsync(
        Guid cloudFolderId,
        string localPath,
        Action<string> reportStatus,
        Action<Exception>? reportFullError,
        Action? onAuthRequired = null,
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
            var mapping = new SyncMappingInfo
            {
                Id = cloudFolderId.ToString("N"),
                CloudFolderId = cloudFolderId,
                LocalRootPath = Path.GetFullPath(localPath)
            };
            await _engine.ReconcileAllAsync(
                new[] { mapping },
                cancellationToken,
                compareSizeAndTime: _compareSizeAndTimeOnSync());
            reportStatus("Sync complete.");
            return true;
        }
        catch (Exception ex) when (IsAuthFailure(ex))
        {
            reportStatus("Please log in.");
            onAuthRequired?.Invoke();
            return false;
        }
        catch (Exception ex)
        {
            reportStatus("Error: " + ex.Message);
            reportFullError?.Invoke(ex);
            return false;
        }
    }

    public async Task<bool> SynchronizeAllAsync(
        IEnumerable<CloudSyncMapping> mappings,
        Action<string> reportStatus,
        Action<Exception>? reportFullError,
        Action? onAuthRequired = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            reportStatus("Syncing…");
            var list = mappings
                .Where(m => !string.IsNullOrWhiteSpace(m.LocalPath) && Directory.Exists(m.LocalPath))
                .Select(m => new SyncMappingInfo
                {
                    Id = m.CloudFolderId.ToString("N"),
                    CloudFolderId = m.CloudFolderId,
                    LocalRootPath = Path.GetFullPath(m.LocalPath),
                    CloudFolderName = m.CloudFolderName ?? ""
                })
                .ToList();
            await _engine.ReconcileAllAsync(
                list,
                cancellationToken,
                compareSizeAndTime: _compareSizeAndTimeOnSync());
            reportStatus("Sync complete.");
            return true;
        }
        catch (Exception ex) when (IsAuthFailure(ex))
        {
            reportStatus("Please log in.");
            onAuthRequired?.Invoke();
            return false;
        }
        catch (Exception ex)
        {
            reportStatus("Error: " + ex.Message);
            reportFullError?.Invoke(ex);
            return false;
        }
    }

    internal static bool IsAuthFailure(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is CloudAuthRequiredException) return true;
            if (e is HttpRequestException http)
            {
                var m = http.Message;
                if (m.Contains("User ID not found", StringComparison.OrdinalIgnoreCase)) return true;
                if (m.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }
}
