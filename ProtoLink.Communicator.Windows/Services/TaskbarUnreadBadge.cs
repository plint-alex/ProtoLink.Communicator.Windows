using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;
using MediaPoint = System.Windows.Point;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>Taskbar overlay badge for total unread messenger count.</summary>
public static class TaskbarUnreadBadge
{
    public static void Apply(Window window, int unreadCount)
    {
        if (window == null) return;
        window.Dispatcher.Invoke(() =>
        {
            window.TaskbarItemInfo ??= new System.Windows.Shell.TaskbarItemInfo();
            if (unreadCount <= 0)
            {
                window.TaskbarItemInfo.Overlay = null;
                return;
            }

            var label = unreadCount > 99 ? "99+" : unreadCount.ToString(CultureInfo.InvariantCulture);
            window.TaskbarItemInfo.Overlay = CreateBadge(label);
        });
    }

    private static ImageSource CreateBadge(string text)
    {
        const int size = 20;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawEllipse(
                new SolidColorBrush(MediaColor.FromRgb(0xE5, 0x39, 0x35)),
                null,
                new MediaPoint(size / 2.0, size / 2.0),
                size / 2.0 - 0.5,
                size / 2.0 - 0.5);
            var ft = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface(new MediaFontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                text.Length > 2 ? 8 : 10,
                MediaBrushes.White,
                VisualTreeHelper.GetDpi(visual).PixelsPerDip);
            dc.DrawText(ft, new MediaPoint((size - ft.Width) / 2.0, (size - ft.Height) / 2.0));
        }

        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }
}
