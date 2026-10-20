using System.Globalization;

namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>Android ChatTime parity for messenger day/time labels and entity list meta.</summary>
public static class ChatTime
{
    public static string FormatTime(DateTime timestamp)
    {
        if (timestamp == default) return string.Empty;
        var local = ToLocal(timestamp);
        return local.ToString("HH:mm", CultureInfo.CurrentCulture);
    }

    public static string FormatDayLabel(DateTime timestamp)
    {
        if (timestamp == default) return string.Empty;
        var local = ToLocal(timestamp);
        var day = local.Date;
        var today = DateTime.Today;
        if (day == today) return "Today";
        if (day == today.AddDays(-1)) return "Yesterday";
        return day.ToString("d MMMM yyyy", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Telegram chat-list style: time today, weekday this week (Mon–Sun window), date otherwise.
    /// </summary>
    public static string FormatListMeta(DateTime timestamp, DateTime? nowLocal = null)
    {
        if (timestamp == default) return string.Empty;
        var local = ToLocal(timestamp);
        var now = nowLocal ?? DateTime.Now;
        var day = local.Date;
        var today = now.Date;
        if (day == today)
            return local.ToString("HH:mm", CultureInfo.CurrentCulture);

        var daysAgo = (today - day).TotalDays;
        if (daysAgo > 0 && daysAgo < 7)
            return local.ToString("ddd", CultureInfo.CurrentCulture);

        if (day.Year == today.Year)
            return local.ToString("d MMM", CultureInfo.CurrentCulture);

        return local.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);
    }

    private static DateTime ToLocal(DateTime timestamp) =>
        timestamp.Kind == DateTimeKind.Utc ? timestamp.ToLocalTime() : timestamp;
}
