using System.IO;
using ProtoLink.Communicator.Windows.Core;

namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// Pure helpers for multi-CloudRoot accounts: pick a healthy My Cloud root, refuse mapping onto CloudRoot,
/// and recover LastOpen / listing failures away from a bloated alternate root (Drive-style duplicate).
/// </summary>
public static class CloudRootSelection
{
    public readonly record struct Candidate(Guid Id, DateTime CreationTime);

    /// <summary>
    /// Prefer a CloudRoot that is not a sync-mapping target; then earliest <see cref="Candidate.CreationTime"/>;
    /// then Id. When <paramref name="preferredId"/> is among the preferred pool, keep it.
    /// </summary>
    public static Guid? PickCanonical(
        IReadOnlyList<Candidate> roots,
        IReadOnlySet<Guid>? mappedCloudFolderIds = null,
        Guid? preferredId = null)
    {
        if (roots == null || roots.Count == 0) return null;
        mappedCloudFolderIds ??= new HashSet<Guid>();

        var nonMapped = roots.Where(r => !mappedCloudFolderIds.Contains(r.Id)).ToList();
        var pool = nonMapped.Count > 0 ? nonMapped : roots;

        if (preferredId is { } pref && pref != Guid.Empty && pool.Any(c => c.Id == pref))
            return pref;

        return pool
            .OrderBy(c => c.CreationTime)
            .ThenBy(c => c.Id)
            .First()
            .Id;
    }

    /// <summary>E6 / notes-and-cloud-sync: map targets a CloudFolder under My Cloud — never a CloudRoot entity.</summary>
    public static bool IsCloudRootCode(string? cloudEntityCode) =>
        string.Equals(cloudEntityCode, CloudEntityCodes.CloudRoot, StringComparison.Ordinal);

    /// <summary>True when <paramref name="cloudFolderId"/> is any known user CloudRoot.</summary>
    public static bool IsKnownCloudRoot(Guid cloudFolderId, IReadOnlySet<Guid>? knownCloudRootIds) =>
        knownCloudRootIds != null && knownCloudRootIds.Contains(cloudFolderId);

    /// <summary>
    /// Refuse Map onto a CloudRoot (by code or known id). Mapping NotesRoot onto a second/bloated CloudRoot
    /// starves interactive GetEntities and surfaces “Waiting for network…”.
    /// </summary>
    public static bool ShouldRefuseMapping(
        string? cloudEntityCode,
        Guid cloudFolderId,
        IReadOnlySet<Guid>? knownCloudRootIds = null) =>
        IsCloudRootCode(cloudEntityCode) || IsKnownCloudRoot(cloudFolderId, knownCloudRootIds);

    /// <summary>
    /// Cold-start LastOpen: do not restore onto a non-canonical CloudRoot when the account has duplicates.
    /// </summary>
    public static bool ShouldSkipLastOpenCloudRoot(
        string? entityCode,
        Guid entityId,
        Guid? canonicalCloudRootId,
        int cloudRootCountUnderUser) =>
        cloudRootCountUnderUser > 1
        && IsCloudRootCode(entityCode)
        && canonicalCloudRootId is { } c
        && c != Guid.Empty
        && entityId != c;

    /// <summary>
    /// After a listing timeout/failure on a CloudRoot row, fall back to the Cloud hub so the banner can clear.
    /// </summary>
    public static bool ShouldFallbackToHubAfterListingFailure(
        Guid listedFolderId,
        IReadOnlySet<Guid>? knownCloudRootIds) =>
        IsKnownCloudRoot(listedFolderId, knownCloudRootIds);

    /// <summary>True when localPath equals NotesRoot after full-path normalize.</summary>
    public static bool IsNotesRootPath(string? localPath, string? notesRootPath)
    {
        if (string.IsNullOrWhiteSpace(localPath) || string.IsNullOrWhiteSpace(notesRootPath))
            return false;
        try
        {
            var a = Path.GetFullPath(localPath.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var b = Path.GetFullPath(notesRootPath.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
