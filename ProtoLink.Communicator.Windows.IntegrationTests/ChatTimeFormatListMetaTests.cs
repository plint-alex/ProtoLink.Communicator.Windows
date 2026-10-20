using System.Globalization;
using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class ChatTimeFormatListMetaTests
{
    [Fact]
    public void Today_ShowsLocalTime()
    {
        var now = new DateTime(2026, 10, 18, 14, 30, 0);
        var stamp = new DateTime(2026, 10, 18, 9, 5, 0);
        Assert.Equal(stamp.ToString("HH:mm", CultureInfo.CurrentCulture), ChatTime.FormatListMeta(stamp, now));
    }

    [Fact]
    public void WithinPastWeek_ShowsWeekday()
    {
        var now = new DateTime(2026, 10, 18, 12, 0, 0); // Sunday
        var stamp = new DateTime(2026, 10, 15, 8, 0, 0); // Wednesday
        Assert.Equal(stamp.ToString("ddd", CultureInfo.CurrentCulture), ChatTime.FormatListMeta(stamp, now));
    }

    [Fact]
    public void SameYearOlderThanWeek_ShowsDayMonth()
    {
        var now = new DateTime(2026, 10, 18, 12, 0, 0);
        var stamp = new DateTime(2026, 3, 4, 8, 0, 0);
        Assert.Equal(stamp.ToString("d MMM", CultureInfo.CurrentCulture), ChatTime.FormatListMeta(stamp, now));
    }

    [Fact]
    public void PriorYear_ShowsFullDate()
    {
        var now = new DateTime(2026, 10, 18, 12, 0, 0);
        var stamp = new DateTime(2025, 12, 1, 8, 0, 0);
        Assert.Equal(stamp.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture), ChatTime.FormatListMeta(stamp, now));
    }

    [Fact]
    public void DefaultTimestamp_IsEmpty()
    {
        Assert.Equal(string.Empty, ChatTime.FormatListMeta(default));
    }
}
