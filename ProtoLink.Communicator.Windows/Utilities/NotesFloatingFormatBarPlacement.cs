namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Pure placement for the Notes floating format bar (Pattern 2 / UX-N-F5).
/// Prefers above the selection so the caret stays visible; clamps inside the editor pane.
/// </summary>
public static class NotesFloatingFormatBarPlacement
{
    public readonly record struct Point(double X, double Y);

    public static Point Place(
        double selectionCenterX,
        double selectionTop,
        double selectionBottom,
        double barWidth,
        double barHeight,
        double paneWidth,
        double paneHeight,
        double gap = 8,
        double margin = 4)
    {
        if (paneWidth <= 0 || paneHeight <= 0 || barWidth <= 0 || barHeight <= 0)
            return new Point(margin, margin);

        var maxX = Math.Max(margin, paneWidth - barWidth - margin);
        var x = Math.Clamp(selectionCenterX - barWidth / 2, margin, maxX);

        var yAbove = selectionTop - gap - barHeight;
        var yBelow = selectionBottom + gap;
        var maxY = Math.Max(margin, paneHeight - barHeight - margin);

        double y;
        if (yAbove >= margin)
            y = yAbove;
        else if (yBelow + barHeight <= paneHeight - margin)
            y = yBelow;
        else
            y = Math.Clamp(yAbove, margin, maxY);

        return new Point(x, y);
    }
}
