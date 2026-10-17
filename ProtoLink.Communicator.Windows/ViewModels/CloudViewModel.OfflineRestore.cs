using System.Linq;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class CloudViewModel
{
    /// <summary>
    /// Cold start: restore <see cref="AppSettings.LastOpenCloudEntityId"/> when it still exists;
    /// otherwise open Cloud root with no error dialog (AT-C7).
    /// </summary>
    public async Task InitializeNavigationAsync()
    {
        if (_authService.CurrentToken == null) return;

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
                    _history.Clear();
                    _locations.Clear();
                    await OpenAsync(open.Entity, open.Breadcrumb, pushHistory: false);
                    return;
                }
            }
            catch
            {
                // Silent fallback to root — no ErrorDetailDialog (AT-C7).
            }
        }

        await EnsureRootAsync();
    }

    private async Task<(CloudEntityRef Entity, IReadOnlyList<CloudBreadcrumbEntry> Breadcrumb)?> TryResolveEntityPathAsync(
        Guid entityId)
    {
        var found = await _api.GetEntitiesByIdsAsync(new[] { entityId }, includeValues: true);
        var entity = found.FirstOrDefault();
        if (entity == null || !CloudEntityRestore.IsNavigableCloudCode(entity.Code))
            return null;

        var chain = new List<CloudEntityRestore.AncestorNode>();
        var current = entity;
        for (var guard = 0; guard < 64; guard++)
        {
            var display = current.Code == CloudEntityCodes.CloudRoot
                ? "Cloud"
                : CloudApiService.GetDisplayName(current);
            chain.Insert(0, new CloudEntityRestore.AncestorNode(current.Id, current.Code ?? "", display));
            if (current.Code == CloudEntityCodes.CloudRoot)
                break;

            var parents = await _api.GetParentEntitiesAsync(current.Id, includeValues: true);
            var parent = parents.FirstOrDefault(p => CloudEntityRestore.IsCloudFolderOrRoot(p.Code));
            if (parent == null)
                return null;
            current = parent;
        }

        return CloudEntityRestore.BuildOpenTarget(chain);
    }

    private void PersistLastOpenCloudEntityId(Guid entityId)
    {
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
    }

    private void SetNetworkBanner()
    {
        _lastError = null;
        OnPropertyChanged(nameof(HasErrorDetails));
        if (_showNetworkBanner) return;
        _showNetworkBanner = true;
        OnPropertyChanged(nameof(ShowNetworkBanner));
        OnPropertyChanged(nameof(ShowStatusBanner));
    }

    private void ShowErrorDetails()
    {
        if (_lastError == null) return;
        Dialogs.ErrorDetailDialog.Show("Cloud", _lastError);
    }
}
