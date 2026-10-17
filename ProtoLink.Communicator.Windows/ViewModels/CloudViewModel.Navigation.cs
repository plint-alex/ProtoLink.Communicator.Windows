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
        await LoadCurrentAsync(quiet: true, restoreSelectionId);
        if (restoreScrollOffset is { } offset)
            ListScrollRestoreRequested?.Invoke(offset);
    }

    private async Task OpenItemAsync(CloudItemViewModel item)
    {
        if (ListedFolderId is not { } parentId || CurrentEntity?.Id == item.Id) return;
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

    private Guid? LinkTargetId(object? parameter) => parameter is CloudItemViewModel item ? item.Id : CurrentEntity?.Id;

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
