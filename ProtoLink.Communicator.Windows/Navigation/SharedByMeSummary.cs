using System.Linq;

namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// Shared by me row subtitle (AC-SO-2): people preview + guest count + public chip.
/// Pure — unit-tested; no AddParent / no second revoke UI.
/// </summary>
public static class SharedByMeSummary
{
    public static string RowSubtitle(
        string? lang,
        IEnumerable<string> peopleNames,
        int peopleMoreCount,
        int liveGuestLinkCount,
        bool hasPublicAccess)
    {
        var parts = new List<string>();
        var people = FormatPeople(peopleNames, peopleMoreCount);
        if (!string.IsNullOrWhiteSpace(people))
            parts.Add(people);
        var guests = FormatGuests(lang, liveGuestLinkCount);
        if (!string.IsNullOrWhiteSpace(guests))
            parts.Add(guests);
        if (hasPublicAccess)
            parts.Add(AnyoneOnInternet(lang));
        return string.Join(" · ", parts);
    }

    public static string FormatPeople(IEnumerable<string> names, int moreCount)
    {
        var cleaned = names
            .Select(n => n?.Trim() ?? string.Empty)
            .Where(n => n.Length > 0)
            .ToList();
        if (cleaned.Count == 0)
            return string.Empty;
        var joined = string.Join(", ", cleaned);
        var more = Math.Max(0, moreCount);
        return more > 0 ? $"{joined} +{more} more" : joined;
    }

    public static string FormatGuests(string? lang, int count)
    {
        if (count < 1)
            return string.Empty;
        if (ShareSheetStrings.IsRussian(lang))
            return count == 1 ? "1 гостевая ссылка" : $"{count} гостевых ссылок";
        return count == 1 ? "1 guest link" : $"{count} guest links";
    }

    public static string AnyoneOnInternet(string? lang) =>
        ShareSheetStrings.IsRussian(lang) ? "Все в интернете" : "Anyone on the internet";
}
