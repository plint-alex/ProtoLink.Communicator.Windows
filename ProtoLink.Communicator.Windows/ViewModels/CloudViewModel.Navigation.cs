using System.Linq;
using System.Diagnostics;
using System.Windows.Input;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class CloudViewModel
{
    /// <summary>Breadcrumb trail of the listed folder at the time an entity was opened; Back restores it.</summary>
    private sealed record CloudLocation(CloudEntityRef Entity, IReadOnlyList<CloudBreadcrumbEntry> Breadcrumb);

    private async Task OpenAsync(
        CloudEntityRef target,
        IEnumerable<CloudBreadcrumbEntry> breadcrumb,
        bool pushHistory = true,
        Guid? restoreSelectionId = null,
        double? restoreScrollOffset = null)
    {
        if (pushHistory && CurrentEntity != null)
            _history.Push(new NavigationEntry<Guid>(CurrentEntity.Id, ListScrollOffset, SelectedItem?.Id ?? Guid.Empty));

        var trail = breadcrumb.Select((b, i) => new CloudBreadcrumbEntry { Index = i, Id = b.Id, Name = b.Name }).ToList();
        _locations[target.Id] = new CloudLocation(target, trail);
        if (!BreadcrumbPath.Select(b => b.Id).SequenceEqual(trail.Select(b => b.Id)))
        {
            BreadcrumbPath.Clear();
            foreach (var b in trail)
                BreadcrumbPath.Add(new CloudBreadcrumbEntry { Index = b.Index, Id = b.Id, Name = b.Name });
        }

        CurrentEntity = target;
        PersistLastOpenCloudEntityId(target.Id);
        RecordRecentOpen(target, pushHistory);
        await LoadCurrentAsync(quiet: true, restoreSelectionId);
        if (restoreScrollOffset is { } offset)
            ListScrollRestoreRequested?.Invoke(offset);
    }

    private void RecordRecentOpen(CloudEntityRef target, bool pushHistory)
    {
        // Forward navigation only — not Back / cold restore / EnsureRoot. Skip virtual hub nodes.
        if (!pushHistory || CloudVirtualNodes.IsVirtual(target.Id)) return;
        try
        {
            _recentStore.RecordOpen(new CloudRecentEntry(
                target.Id,
                target.Name,
                target.IsFolder,
                DateTime.UtcNow));
        }
        catch
        {
            // Best-effort; navigation must not fail.
        }
    }

    private async Task OpenItemAsync(CloudItemViewModel item)
    {
        if (ListedFolderId is not { } parentId || CurrentEntity?.Id == item.Id) return;

        // Recent rows are not children of the virtual folder — open via real Cloud path (silent skip if gone).
        if (parentId == CloudVirtualNodes.RecentId)
        {
            if (await TryOpenEntityAsync(item.Id))
                EntityOpened?.Invoke();
            return;
        }

        var kind = item.IsFolder ? CloudEntityKind.Folder : CloudEntityKind.File;
        var trail = item.IsFolder
            ? BreadcrumbPath.Append(new CloudBreadcrumbEntry { Id = item.Id, Name = item.Name })
            : BreadcrumbPath;
        await OpenAsync(new CloudEntityRef(item.Id, kind, item.Name, parentId), trail.ToList());
        EntityOpened?.Invoke();
    }

    private async Task NavigateToBreadcrumbAsync(int index)
    {
        if (index < 0 || index >= BreadcrumbPath.Count) return;
        var entry = BreadcrumbPath[index];
        if (CurrentEntity?.Id == entry.Id) return;
        var parentId = index > 0 ? BreadcrumbPath[index - 1].Id : (Guid?)null;
        await OpenAsync(
            new CloudEntityRef(entry.Id, CloudEntityKind.Folder, entry.Name, parentId),
            BreadcrumbPath.Take(index + 1).ToList());
        EntityOpened?.Invoke();
    }

    private async Task NavigateToParentAsync()
    {
        if (BreadcrumbPath.Count < 2) return;
        await NavigateToBreadcrumbAsync(BreadcrumbPath.Count - 2);
    }

    private async Task GoBackAsync()
    {
        while (_history.TryPop(out var entry))
        {
            if (!_locations.TryGetValue(entry.Key, out var location)) continue;
            await OpenAsync(
                location.Entity,
                location.Breadcrumb,
                pushHistory: false,
                restoreSelectionId: entry.SelectedKey == Guid.Empty ? null : entry.SelectedKey,
                restoreScrollOffset: entry.ListScrollOffset);
            return;
        }
    }

    private Guid? LinkTargetId(object? parameter)
    {
        var id = parameter is CloudItemViewModel item ? item.Id : CurrentEntity?.Id;
        return id is { } g && !CloudVirtualNodes.IsVirtual(g) ? g : null;
    }

    private Guid? ShareTargetId(object? parameter)
    {
        if (parameter is CloudItemViewModel row && row.IsVirtualEntry)
            return null;
        var id = parameter is CloudItemViewModel item ? item.Id : CurrentEntity?.Id;
        return id is { } g && !CloudVirtualNodes.IsVirtual(g) ? g : null;
    }

    private async Task OpenShareAsync(Guid? entityId)
    {
        if (entityId is not { } id)
        {
            StatusText = ShareSheetStrings.NotInCloudYet(_settingsService.LoadSettings().UiLanguage);
            return;
        }

        string name;
        bool isFolder;
        int childCount;
        if (CurrentEntity?.Id == id)
        {
            name = CurrentEntity.Name;
            isFolder = CurrentEntity.IsFolder;
            childCount = isFolder ? ListedChildCount : 0;
        }
        else if (Items.FirstOrDefault(i => i.Id == id) is { } row)
        {
            name = row.Name;
            isFolder = row.IsFolder;
            childCount = 0;
        }
        else
        {
            name = id.ToString("D");
            isFolder = true;
            childCount = 0;
        }

        // Cycle 1: any signed-in Cloud user who can open the entity may attempt share;
        // API returns 403 when the caller cannot write (sheet shows viewer footer after load fail).
        try
        {
            await ShareSheet.OpenAsync(
                id,
                name,
                isFolder,
                childCount,
                canEdit: true,
                isOnline: !_showNetworkBanner);
            OnPropertyChanged(nameof(ShareSheet));
        }
        catch (Exception ex)
        {
            StatusText = ShareSheetStrings.LoadFailed(_settingsService.LoadSettings().UiLanguage) + " " + ex.Message;
        }
    }

    private string BuildLink(Guid entityId)
    {
        var settings = _settingsService.LoadSettings();
        return EntityLinks.Build(settings.PublicSiteBaseUrl, entityId, settings.UiLanguage);
    }

    private void CopyLink(Guid? entityId)
    {
        if (entityId is not { } id) return;
        try
        {
            System.Windows.Clipboard.SetText(BuildLink(id));
            StatusText = "Link copied";
        }
        catch (Exception ex)
        {
            StatusText = "Could not copy link: " + ex.Message;
        }
    }

    private void OpenInBrowser(Guid? entityId)
    {
        if (entityId is not { } id) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = BuildLink(id), UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = "Could not open browser: " + ex.Message;
        }
    }
}
