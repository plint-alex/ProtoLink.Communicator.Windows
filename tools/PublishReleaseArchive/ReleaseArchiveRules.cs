using System.Globalization;
using System.Text.RegularExpressions;

namespace PublishReleaseArchive;

/// <summary>
/// Pure rules for D10 publish snapshot + prune (communicator-release-history).
/// Spec: Agents docs/features/downloads-news-page § Version history.
/// </summary>
public static class ReleaseArchiveRules
{
    public const int MaxArchivePerPlatform = 5;

    private static readonly Regex WindowsSetupVersion = new(
        @"^ProtoLink\.Communicator\.Windows-Setup-(.+)\.exe$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AndroidApkVersion = new(
        @"^ProtoLink\.Communicator\.Android-(.+)\.apk$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Snapshot when current version entity name is non-empty and differs from the version about to publish.
    /// </summary>
    public static bool ShouldSnapshot(string? currentVersion, string? newVersion)
    {
        if (string.IsNullOrWhiteSpace(currentVersion))
            return false;
        if (string.IsNullOrWhiteSpace(newVersion))
            return false;
        return !string.Equals(currentVersion.Trim(), newVersion.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Parse semver (or version label) from a versioned artifact filename.
    /// </summary>
    public static string? TryParseSemverFromFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var name = fileName.Trim();
        var win = WindowsSetupVersion.Match(name);
        if (win.Success)
            return win.Groups[1].Value;

        var android = AndroidApkVersion.Match(name);
        if (android.Success)
            return android.Groups[1].Value;

        return null;
    }

    /// <summary>
    /// Returns archive row ids to delete so that at most <paramref name="maxKeep"/> remain.
    /// Oldest = lowest semver; tie-break earliest CreationTime; then id ordinal.
    /// </summary>
    public static IReadOnlyList<string> SelectOldestToPrune(
        IEnumerable<ArchiveRow> rows,
        int maxKeep = MaxArchivePerPlatform)
    {
        if (maxKeep < 0)
            throw new ArgumentOutOfRangeException(nameof(maxKeep));

        var list = rows?.ToList() ?? new List<ArchiveRow>();
        if (list.Count <= maxKeep)
            return Array.Empty<string>();

        var ordered = list
            .OrderBy(r => ParseSemverKey(r.Semver), SemverKeyComparer.Instance)
            .ThenBy(r => r.CreationTimeUtc)
            .ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var excess = ordered.Count - maxKeep;
        return ordered.Take(excess).Select(r => r.Id).ToList();
    }

    public readonly record struct ArchiveRow(string Id, string Semver, DateTime CreationTimeUtc);

    private readonly record struct SemverKey(int Major, int Minor, int Patch, string Remainder, string Raw);

    private static SemverKey ParseSemverKey(string? semver)
    {
        var raw = (semver ?? string.Empty).Trim();
        if (raw.Length == 0)
            return new SemverKey(-1, -1, -1, string.Empty, raw);

        var core = raw;
        var remainder = string.Empty;
        var dash = raw.IndexOf('-');
        var plus = raw.IndexOf('+');
        var cut = -1;
        if (dash >= 0 && plus >= 0)
            cut = Math.Min(dash, plus);
        else if (dash >= 0)
            cut = dash;
        else if (plus >= 0)
            cut = plus;
        if (cut >= 0)
        {
            core = raw[..cut];
            remainder = raw[cut..];
        }

        var parts = core.Split('.');
        static int Part(string[] p, int i) =>
            i < p.Length && int.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? n
                : -1;

        return new SemverKey(Part(parts, 0), Part(parts, 1), Part(parts, 2), remainder, raw);
    }

    private sealed class SemverKeyComparer : IComparer<SemverKey>
    {
        public static readonly SemverKeyComparer Instance = new();

        public int Compare(SemverKey x, SemverKey y)
        {
            var c = x.Major.CompareTo(y.Major);
            if (c != 0) return c;
            c = x.Minor.CompareTo(y.Minor);
            if (c != 0) return c;
            c = x.Patch.CompareTo(y.Patch);
            if (c != 0) return c;
            c = string.CompareOrdinal(x.Remainder, y.Remainder);
            if (c != 0) return c;
            return string.CompareOrdinal(x.Raw, y.Raw);
        }
    }
}
