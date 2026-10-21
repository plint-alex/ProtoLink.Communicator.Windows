using PublishReleaseArchive;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class ReleaseArchiveRulesTests
{
    [Theory]
    [InlineData(null, "1.0.8", false)]
    [InlineData("", "1.0.8", false)]
    [InlineData("   ", "1.0.8", false)]
    [InlineData("1.0.7", "1.0.7", false)]
    [InlineData("1.0.7", "1.0.8", true)]
    [InlineData("1.0.8", "1.0.7", true)]
    [InlineData(" 1.0.7 ", "1.0.7", false)]
    public void ShouldSnapshot_MatchesD10(string? current, string? next, bool expected)
    {
        Assert.Equal(expected, ReleaseArchiveRules.ShouldSnapshot(current, next));
    }

    [Theory]
    [InlineData("ProtoLink.Communicator.Windows-Setup-1.0.7.exe", "1.0.7")]
    [InlineData("ProtoLink.Communicator.Windows-Setup-1.0.8-stand.exe", "1.0.8-stand")]
    [InlineData("ProtoLink.Communicator.Android-1.0.3.apk", "1.0.3")]
    [InlineData("ProtoLink.Communicator.Windows-Setup.exe", null)]
    [InlineData("ProtoLink.Communicator.Android.apk", null)]
    [InlineData("", null)]
    public void TryParseSemverFromFileName(string fileName, string? expected)
    {
        Assert.Equal(expected, ReleaseArchiveRules.TryParseSemverFromFileName(fileName));
    }

    [Fact]
    public void SelectOldestToPrune_EmptyWhenAtOrUnderCap()
    {
        var rows = new[]
        {
            new ReleaseArchiveRules.ArchiveRow("a", "1.0.1", DateTime.UtcNow),
            new ReleaseArchiveRules.ArchiveRow("b", "1.0.2", DateTime.UtcNow),
        };
        Assert.Empty(ReleaseArchiveRules.SelectOldestToPrune(rows, maxKeep: 5));
        Assert.Empty(ReleaseArchiveRules.SelectOldestToPrune(rows, maxKeep: 2));
    }

    [Fact]
    public void SelectOldestToPrune_RemovesLowestSemverFirst()
    {
        var t = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            new ReleaseArchiveRules.ArchiveRow("keep-high", "1.0.5", t),
            new ReleaseArchiveRules.ArchiveRow("drop-1", "1.0.1", t),
            new ReleaseArchiveRules.ArchiveRow("keep-mid", "1.0.3", t),
            new ReleaseArchiveRules.ArchiveRow("drop-2", "1.0.2", t),
            new ReleaseArchiveRules.ArchiveRow("keep-4", "1.0.4", t),
            new ReleaseArchiveRules.ArchiveRow("keep-6", "1.0.6", t),
        };

        var prune = ReleaseArchiveRules.SelectOldestToPrune(rows, maxKeep: 5);
        Assert.Equal(new[] { "drop-1" }, prune);
    }

    [Fact]
    public void SelectOldestToPrune_TieBreaksByCreationTimeThenId()
    {
        var early = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var late = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            new ReleaseArchiveRules.ArchiveRow("b-late", "1.0.1", late),
            new ReleaseArchiveRules.ArchiveRow("a-early", "1.0.1", early),
            new ReleaseArchiveRules.ArchiveRow("keep-2", "1.0.2", late),
            new ReleaseArchiveRules.ArchiveRow("keep-3", "1.0.3", late),
            new ReleaseArchiveRules.ArchiveRow("keep-4", "1.0.4", late),
            new ReleaseArchiveRules.ArchiveRow("keep-5", "1.0.5", late),
        };

        var prune = ReleaseArchiveRules.SelectOldestToPrune(rows, maxKeep: 5);
        Assert.Equal(new[] { "a-early" }, prune);
    }

    [Fact]
    public void MaxArchivePerPlatform_IsFive()
    {
        Assert.Equal(5, ReleaseArchiveRules.MaxArchivePerPlatform);
    }
}
