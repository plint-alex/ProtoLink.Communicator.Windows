using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class NotesFloatingFormatBarPlacementTests
{
    [Fact]
    public void Place_PrefersAboveSelection_WhenRoomExists()
    {
        var p = NotesFloatingFormatBarPlacement.Place(
            selectionCenterX: 200,
            selectionTop: 120,
            selectionBottom: 140,
            barWidth: 400,
            barHeight: 44,
            paneWidth: 800,
            paneHeight: 600);

        Assert.Equal(4, p.X, 1); // 200 - 200 = 0 → clamped to margin 4
        Assert.True(p.Y < 120);
        Assert.Equal(120 - 8 - 44, p.Y, 1);
    }

    [Fact]
    public void Place_FallsBelow_WhenNotEnoughRoomAbove()
    {
        var p = NotesFloatingFormatBarPlacement.Place(
            selectionCenterX: 400,
            selectionTop: 20,
            selectionBottom: 40,
            barWidth: 400,
            barHeight: 44,
            paneWidth: 800,
            paneHeight: 600);

        Assert.Equal(200, p.X, 1);
        Assert.Equal(48, p.Y, 1); // bottom 40 + gap 8
    }

    [Fact]
    public void Place_ClampsHorizontally_InsideNarrowPane()
    {
        var p = NotesFloatingFormatBarPlacement.Place(
            selectionCenterX: 350,
            selectionTop: 200,
            selectionBottom: 220,
            barWidth: 480,
            barHeight: 44,
            paneWidth: 500,
            paneHeight: 700);

        Assert.True(p.X >= 4);
        Assert.True(p.X + 480 <= 500 - 4 + 0.01);
    }

    [Fact]
    public void Place_DoesNotCoverCaretPermanently_WhenClampedVertically()
    {
        var p = NotesFloatingFormatBarPlacement.Place(
            selectionCenterX: 100,
            selectionTop: 10,
            selectionBottom: 580,
            barWidth: 200,
            barHeight: 44,
            paneWidth: 400,
            paneHeight: 600);

        Assert.True(p.Y >= 4);
        Assert.True(p.Y + 44 <= 600 - 4 + 0.01);
    }
}
