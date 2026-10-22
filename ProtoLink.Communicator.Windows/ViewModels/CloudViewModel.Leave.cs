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

    private async Task NavigateToSharedWithMeListingAsync()
    {
        var lang = _settingsService.LoadSettings().UiLanguage;
        var hubName = ShareSheetStrings.CloudHub(lang);
        var sharedName = ShareSheetStrings.SharedWithMe(lang);
        await OpenAsync(
            new CloudEntityRef(
                CloudVirtualNodes.SharedWithMeId,
                CloudEntityKind.Folder,
                sharedName,
                CloudVirtualNodes.HubId),
            new[]
            {
                new CloudBreadcrumbEntry { Id = CloudVirtualNodes.HubId, Name = hubName },
                new CloudBreadcrumbEntry { Id = CloudVirtualNodes.SharedWithMeId, Name = sharedName }
            },
            pushHistory: false);
    }
}
