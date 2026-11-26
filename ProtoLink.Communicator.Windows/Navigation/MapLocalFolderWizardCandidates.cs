using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Dialogs;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Sync;

namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// Pure E6 wizard pick-list shaping. Interactive listing only — never unbounded
/// SameNamed cross-root discovery (that blocked ShowDialog / AT-NMR-7).
/// </summary>
public static class MapLocalFolderWizardCandidates
{
    /// <summary>
    /// Unmapped CloudFolder rows from a parent listing page, with short-id labels when names collide
    /// within this page.
    /// </summary>
    public static IReadOnlyList<MapLocalFolderCandidate> FromParentListing(
        IEnumerable<GetEntitiesResult>? entities,
        IReadOnlySet<Guid> mappedFolderIds,
        Func<Guid, bool>? isFolderSynced = null)
    {
        mappedFolderIds ??= new HashSet<Guid>();
        isFolderSynced ??= _ => false;

        var folders = new List<(Guid Id, string Name)>();
        foreach (var e in entities ?? Array.Empty<GetEntitiesResult>())
        {
            if (!CloudEntityClassify.IsFolder(e.TypeEntityId, e.Code)) continue;
            if (CloudEntityClassify.IsCloudRootCode(e.Code)) continue;
            if (mappedFolderIds.Contains(e.Id) || isFolderSynced(e.Id)) continue;
            folders.Add((e.Id, CloudApiService.GetDisplayName(e)));
        }

        var names = folders.Select(f => f.Name).ToList();
        return folders
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Id)
            .Select(f => new MapLocalFolderCandidate
            {
                Id = f.Id,
                Name = SameNamedCloudFolderHeal.DisplayLabel(f.Name, f.Id, names)
            })
            .ToList();
    }

    /// <summary>
    /// Prefer an existing same-named child under the create parent (first interactive pages).
    /// Cross-CloudRoot duals stay on Sync SameNamed heal — do not block wizard confirm.
    /// </summary>
    public static (Guid Id, string Name)? PreferSameNamedUnderParent(
        string desiredName,
        IEnumerable<GetEntitiesResult>? entities)
    {
        var want = (desiredName ?? "").Trim();
        if (want.Length == 0) return null;

        foreach (var e in entities ?? Array.Empty<GetEntitiesResult>())
        {
            if (!CloudEntityClassify.IsFolder(e.TypeEntityId, e.Code)) continue;
            if (CloudEntityClassify.IsCloudRootCode(e.Code)) continue;
            var name = CloudApiService.GetDisplayName(e);
            if (string.Equals(name.Trim(), want, StringComparison.OrdinalIgnoreCase))
                return (e.Id, string.IsNullOrWhiteSpace(name) ? want : name);
        }

        return null;
    }
}
