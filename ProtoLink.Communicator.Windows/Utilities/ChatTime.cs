using System.Globalization;

namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>Android ChatTime parity for messenger day/time labels.</summary>
public static class ChatTime
{
    public static string FormatTime(DateTime timestamp)
    {
        if (timestamp == default) return string.Empty;
        var local = timestamp.Kind == DateTimeKind.Utc ? timestamp.ToLocalTime() : timestamp;
        return local.ToString("HH:mm", CultureInfo.CurrentCulture);
    }

    public static string FormatDayLabel(DateTime timestamp)
    {
        if (timestamp == default) return string.Empty;
        var local = timestamp.Kind == DateTimeKind.Utc ? timestamp.ToLocalTime() : timestamp;
        var day = local.Date;
        var today = DateTime.Today;
        if (day == today) return "Today";
        if (day == today.AddDays(-1)) return "Yesterday";
        return day.ToString("d MMMM yyyy", CultureInfo.CurrentCulture);
    }
}
