using System.Linq;
using System.Windows.Input;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class CloudViewModel
{
    private bool _leaveBusy;

    /// <summary>UX-LS-1: open Shared-with-me share root may Leave via header overflow.</summary>
    public bool CanLeaveCurrentSharedRoot =>
        CloudSharedScope.CanLeaveOpenShareRoot(
            CurrentEntity?.Id,
            BreadcrumbPath.Select(b => b.Id),
            _shareRootCanWrite);

    public bool CanLeaveSharedRow(CloudItemViewModel item) =>
        CloudSharedScope.CanLeaveSharedListRow(IsSharedWithMeListing, item.IsShareRoot, item.IsVirtualEntry);

    /// <summary>Parameter: share-root row, or null for <see cref="CurrentEntity"/> when it is the active share root.</summary>
    public ICommand LeaveSharedCommand { get; private set; } = null!;

    /// <summary>E9f Locations — open Shared with me virtual listing.</summary>
    public ICommand OpenSharedWithMeCommand { get; private set; } = null!;

    /// <summary>E9f Locations — open Shared by me virtual listing.</summary>
    public ICommand OpenSharedByMeCommand { get; private set; } = null!;

    /// <summary>E9f Locations — open Recent virtual listing.</summary>
    public ICommand OpenRecentCommand { get; private set; } = null!;

    public string OpenSharedWithMeLabel =>
        ShareSheetStrings.SharedWithMe(_settingsService.LoadSettings().UiLanguage);

    public string OpenSharedByMeLabel =>
        ShareSheetStrings.SharedByMe(_settingsService.LoadSettings().UiLanguage);

    public string OpenRecentLabel =>
        ShareSheetStrings.Recent(_settingsService.LoadSettings().UiLanguage);

    private void InitLeaveCommands()
    {
        LeaveSharedCommand = new RelayCommand(
            async p =>
            {
                var id = LeaveTargetId(p);
                if (id is not { } entityId) return;
                await LeaveSharedAsync(entityId);
            },
            p => !_leaveBusy && LeaveTargetId(p).HasValue);
        OpenSharedWithMeCommand = new RelayCommand(
            async _ => await NavigateToSharedWithMeListingAsync(),
            _ => !IsSharedWithMeListing);
        OpenSharedByMeCommand = new RelayCommand(
            async _ => await NavigateToSharedByMeListingAsync(),
            _ => !IsSharedByMeListing);
        OpenRecentCommand = new RelayCommand(
            async _ => await NavigateToRecentListingAsync(),
            _ => !IsRecentListing);
    }

    private Guid? LeaveTargetId(object? parameter)
    {
        if (parameter is CloudItemViewModel row)
            return CanLeaveSharedRow(row) ? row.Id : null;
        return CanLeaveCurrentSharedRoot ? CurrentEntity!.Id : null;
    }

    public async Task LeaveSharedAsync(Guid entityId)
    {
        if (_leaveBusy || entityId == Guid.Empty) return;
        var lang = _settingsService.LoadSettings().UiLanguage;
        var wasOpenRoot = CurrentEntity?.Id == entityId;
        _leaveBusy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await _api.LeaveSharedWithMeAsync(entityId);
            _shareRootCaptions.Remove(entityId);
            _shareRootCanWrite.Remove(entityId);

            var row = Items.FirstOrDefault(i => i.Id == entityId);
            if (row != null)
                Items.Remove(row);

            StatusText = ShareSheetStrings.LeaveSuccess(lang);
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ListedChildCount));
            OnPropertyChanged(nameof(ListedChildCountText));
            OnPropertyChanged(nameof(CanLeaveCurrentSharedRoot));
            OnPropertyChanged(nameof(ShowSharedBrowseOnlyHint));
            OnPropertyChanged(nameof(CanMapCurrentFolder));
            OnPropertyChanged(nameof(HeaderSubtitle));

            if (wasOpenRoot || CurrentEntity?.Id == entityId)
                await NavigateToSharedWithMeListingAsync();
            else if (IsSharedWithMeListing)
                await LoadCurrentAsync(quiet: true);
        }
        catch (Exception ex)
        {
            StatusText = ShareSheetStrings.LeaveFailed(lang);
            _lastError = ex;
            OnPropertyChanged(nameof(HasErrorDetails));
            OnPropertyChanged(nameof(ShowStatusBanner));
        }
        finally
        {
            _leaveBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task NavigateToSharedWithMeListingAsync() =>
        await OpenVirtualLocationAsync(
            CloudVirtualNodes.SharedWithMeId,
            ShareSheetStrings.SharedWithMe(_settingsService.LoadSettings().UiLanguage));

    private async Task NavigateToSharedByMeListingAsync() =>
        await OpenVirtualLocationAsync(
            CloudVirtualNodes.SharedByMeId,
            ShareSheetStrings.SharedByMe(_settingsService.LoadSettings().UiLanguage));

    private async Task NavigateToRecentListingAsync() =>
        await OpenVirtualLocationAsync(
            CloudVirtualNodes.RecentId,
            ShareSheetStrings.Recent(_settingsService.LoadSettings().UiLanguage));

    /// <summary>E9f: SWM / SBM / Recent as secondary Locations — single crumb, no hub parent.</summary>
    private async Task OpenVirtualLocationAsync(Guid virtualId, string name)
    {
        await OpenAsync(
            new CloudEntityRef(virtualId, CloudEntityKind.Folder, name, null),
            new[] { new CloudBreadcrumbEntry { Id = virtualId, Name = name } },
            pushHistory: true);
    }
}
