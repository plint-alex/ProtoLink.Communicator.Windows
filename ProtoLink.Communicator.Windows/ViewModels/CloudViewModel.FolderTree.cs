using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class CloudViewModel
{
    private Guid? _photosLibraryId;
    private Guid? _folderTreeRootId;
    private readonly Dictionary<Guid, string> _folderTreeCodes = new();

    public ObservableCollection<CloudFolderTreeRowViewModel> FolderTreeRows { get; } = new();

    public bool ShowFolderTree => FolderTreeRows.Count > 0;

    public string FolderTreeHeading =>
        ShareSheetStrings.FoldersTree(_settingsService.LoadSettings().UiLanguage);

    public ICommand OpenFolderTreeCommand { get; private set; } = null!;

    private void InitFolderTreeCommands()
    {
        OpenFolderTreeCommand = new RelayCommand(
            async p => await OpenFolderTreeRowAsync((CloudFolderTreeRowViewModel)p!),
            p => p is CloudFolderTreeRowViewModel);
    }

    private async Task EnsurePhotosLibraryIdAsync()
    {
        if (_photosLibraryId != null || _authService.CurrentToken == null) return;
        try
        {
            var userId = _authService.CurrentToken.UserId;
            var kids = await _api.GetEntitiesAsync(new[] { userId }, includeValues: true);
            _photosLibraryId = PhotosLibrary.ResolveLibraryIdFromUserChildren(kids);
        }
        catch
        {
            // Best-effort; hub Photos row omitted when unresolved.
        }
    }

    private void ApplyOptimisticFolderTreeSelection(Guid? folderId)
    {
        if (FolderTreeRows.Count == 0) return;
        foreach (var r in FolderTreeRows)
            r.IsSelected = folderId is { } id && r.Id == id;
    }

    private async Task OpenFolderTreeRowAsync(CloudFolderTreeRowViewModel row)
    {
        if (CurrentEntity?.Id == row.Id) return;

        // Paint tree selection immediately — never wait on ancestry GetEntities first.
        ApplyOptimisticFolderTreeSelection(row.Id);

        var treeRows = FolderTreeRows.Select(r => (r.Id, r.Name, r.Depth)).ToList();
        var ancestors = CloudFolderNavigation.BuildAncestorTrail(treeRows, row.Id);
        if (ancestors.Count > 0)
        {
            var composed = CloudFolderNavigation.ComposeBreadcrumb(
                BreadcrumbPath.Select(b => (b.Id, b.Name)).ToList(),
                ancestors);
            Guid? parentId = composed.Count >= 2 ? composed[^2].Id : null;
            var trail = composed
                .Select(c => new CloudBreadcrumbEntry { Id = c.Id, Name = c.Name })
                .ToList();
            await OpenAsync(
                new CloudEntityRef(row.Id, CloudEntityKind.Folder, row.Name, parentId),
                trail);
            EntityOpened?.Invoke();
            return;
        }

        BeginFolderListingLoad();
        await YieldForChromePaintAsync();
        if (await TryOpenEntityAsync(row.Id))
            EntityOpened?.Invoke();
    }

    private async Task RefreshFolderTreeAsync()
    {
        if (ListedFolderId is not { } listed || CloudVirtualNodes.IsVirtual(listed))
        {
            ClearFolderTree();
            return;
        }

        await EnsurePhotosLibraryIdAsync();
        var crumbMeta = BreadcrumbPath
            .Select(b =>
            {
                _folderTreeCodes.TryGetValue(b.Id, out var code);
                if (b.Id == _myCloudRootId) code = CloudEntityCodes.CloudRoot;
                return (b.Id, code);
            })
            .ToList();

        var rootId = CloudFolderTree.PickTreeRootId(
            crumbMeta,
            _photosLibraryId,
            CloudVirtualNodes.IsVirtual);
        if (rootId is not { } treeRoot || treeRoot == Guid.Empty)
        {
            ClearFolderTree();
            return;
        }

        try
        {
            var childrenByParent = new Dictionary<Guid, IReadOnlyList<CloudFolderTree.Node>>();
            var names = new Dictionary<Guid, string>();
            var queue = new Queue<Guid>();
            queue.Enqueue(treeRoot);
            names[treeRoot] = BreadcrumbPath.FirstOrDefault(b => b.Id == treeRoot)?.Name
                              ?? (_photosLibraryId == treeRoot
                                  ? ShareSheetStrings.Photos(_settingsService.LoadSettings().UiLanguage)
                                  : "Cloud");
            var folderCount = 0;

            while (queue.Count > 0 && folderCount < CloudFolderTree.DefaultMaxFolders)
            {
                var parentId = queue.Dequeue();
                // Values required so GetDisplayName resolves folder name slots (not folder-{id}).
                var list = await _api.GetEntitiesAsync(new[] { parentId }, includeValues: true);
                if (ListedFolderId is not { } still || CloudVirtualNodes.IsVirtual(still))
                {
                    ClearFolderTree();
                    return;
                }

                var folderKids = new List<CloudFolderTree.Node>();
                foreach (var e in (list ?? new List<GetEntitiesResult>())
                         .Where(x => CloudFolderTree.IsFolderTreeCode(x.Code))
                         .OrderBy(x => CloudApiService.GetDisplayName(x), StringComparer.OrdinalIgnoreCase))
                {
                    _folderTreeCodes[e.Id] = e.Code ?? CloudEntityCodes.CloudFolder;
                    var name = CloudApiService.GetDisplayName(e);
                    names[e.Id] = name;
                    folderKids.Add(new CloudFolderTree.Node(e.Id, name));
                    if (folderCount < CloudFolderTree.DefaultMaxFolders)
                    {
                        queue.Enqueue(e.Id);
                        folderCount++;
                    }
                }

                childrenByParent[parentId] = folderKids;
            }

            var selected = CloudFolderTree.SelectedFolderId(
                IsFileOpen,
                CurrentEntity?.Id,
                CurrentEntity?.ParentId);
            var flat = CloudFolderTree.Flatten(
                new CloudFolderTree.Node(treeRoot, names.GetValueOrDefault(treeRoot, "Cloud")),
                childrenByParent,
                selected);

            FolderTreeRows.Clear();
            foreach (var row in flat)
            {
                FolderTreeRows.Add(new CloudFolderTreeRowViewModel
                {
                    Id = row.Id,
                    Name = row.Name,
                    Depth = row.Depth,
                    IsSelected = row.Selected
                });
            }

            _folderTreeRootId = treeRoot;
            OnPropertyChanged(nameof(ShowFolderTree));
            OnPropertyChanged(nameof(FolderTreeHeading));
        }
        catch
        {
            // Keep prior tree on transient failure.
        }
    }

    private void ClearFolderTree()
    {
        if (FolderTreeRows.Count == 0 && _folderTreeRootId == null) return;
        FolderTreeRows.Clear();
        _folderTreeRootId = null;
        OnPropertyChanged(nameof(ShowFolderTree));
    }
}
