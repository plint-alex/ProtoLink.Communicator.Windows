using System.Text.Json;
using PublishReleaseArchive;

// CLI bridge for Publish-Release-To-Cloud.ps1 (same rules as ReleaseArchiveRules).
//   should-snapshot <currentVersion> <newVersion>   → prints true|false
//   parse-semver <fileName>                         → prints semver or empty line
//   select-prune <maxKeep> <jsonArray>              → prints ids to prune (one per line)
// jsonArray item: { "id", "semver", "creationTimeUtc" }

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: should-snapshot | parse-semver | select-prune");
    return 1;
}

var cmd = args[0].Trim().ToLowerInvariant();
switch (cmd)
{
    case "should-snapshot":
    {
        // Windows PowerShell 5.1 strips empty native argv, so empty current may arrive
        // as only [should-snapshot, newVersion]. Pad missing slots with "" — ShouldSnapshot
        // is false whenever either side is empty/whitespace (order of a single leftover arg
        // does not matter).
        var current = args.Length > 1 ? args[1] : string.Empty;
        var next = args.Length > 2 ? args[2] : string.Empty;
        var ok = ReleaseArchiveRules.ShouldSnapshot(current, next);
        Console.WriteLine(ok ? "true" : "false");
        return 0;
    }
    case "parse-semver":
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("parse-semver <fileName>");
            return 1;
        }
        Console.WriteLine(ReleaseArchiveRules.TryParseSemverFromFileName(args[1]) ?? string.Empty);
        return 0;
    }
    case "select-prune":
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("select-prune <maxKeep> <jsonArray>");
            return 1;
        }
        if (!int.TryParse(args[1], out var maxKeep))
        {
            Console.Error.WriteLine("maxKeep must be an integer");
            return 1;
        }
        var rows = JsonSerializer.Deserialize<List<PruneDto>>(args[2], new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<PruneDto>();
        var mapped = rows.Select(r => new ReleaseArchiveRules.ArchiveRow(
            r.Id ?? string.Empty,
            r.Semver ?? string.Empty,
            r.CreationTimeUtc == default ? DateTime.MinValue : DateTime.SpecifyKind(r.CreationTimeUtc, DateTimeKind.Utc)
        ));
        foreach (var id in ReleaseArchiveRules.SelectOldestToPrune(mapped, maxKeep))
            Console.WriteLine(id);
        return 0;
    }
    default:
        Console.Error.WriteLine($"Unknown command: {args[0]}");
        return 1;
}

file sealed class PruneDto
{
    public string? Id { get; set; }
    public string? Semver { get; set; }
    public DateTime CreationTimeUtc { get; set; }
}
