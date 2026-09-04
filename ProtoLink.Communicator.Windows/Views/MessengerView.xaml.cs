using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ProtoLink.Communicator.Windows.ViewModels;

namespace ProtoLink.Communicator.Windows.Views;

public partial class MessengerView : System.Windows.Controls.UserControl
{
    private DispatcherTimer? _stickyHideTimer;
    private bool _stickToBottom = true;

    public MessengerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        HookMessagesCollection();
        ScrollMessagesToEnd();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnhookMessagesCollection();
        _stickyHideTimer?.Stop();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MessengerViewModel oldVm)
            oldVm.Messages.CollectionChanged -= OnMessagesCollectionChanged;
        HookMessagesCollection();
        ScrollMessagesToEnd();
    }

    private void HookMessagesCollection()
    {
        if (DataContext is MessengerViewModel vm)
        {
            vm.Messages.CollectionChanged -= OnMessagesCollectionChanged;
            vm.Messages.CollectionChanged += OnMessagesCollectionChanged;
        }
    }

    private void UnhookMessagesCollection()
    {
        if (DataContext is MessengerViewModel vm)
            vm.Messages.CollectionChanged -= OnMessagesCollectionChanged;
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_stickToBottom)
            Dispatcher.BeginInvoke(ScrollMessagesToEnd, DispatcherPriority.Background);
    }

    private void ScrollMessagesToEnd()
    {
        if (MessagesList.Items.Count == 0) return;
        MessagesList.ScrollIntoView(MessagesList.Items[^1]);
    }

    private void OnMessagesScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange != 0 && _stickToBottom)
            ScrollMessagesToEnd();

        var viewer = FindScrollViewer(MessagesList);
        if (viewer != null)
        {
            var distanceFromBottom = viewer.ExtentHeight - viewer.VerticalOffset - viewer.ViewportHeight;
            _stickToBottom = distanceFromBottom < 40;
        }

        // Sticky date only while the user is actively scrolling (Android parity).
        if (Math.Abs(e.VerticalChange) < 0.1 && e.ExtentHeightChange == 0)
            return;

        UpdateStickyDate();
        StickyDateChip.Visibility = Visibility.Visible;
        _stickyHideTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _stickyHideTimer.Tick -= OnStickyHideTick;
        _stickyHideTimer.Tick += OnStickyHideTick;
        _stickyHideTimer.Stop();
        _stickyHideTimer.Start();
    }

    private void OnStickyHideTick(object? sender, EventArgs e)
    {
        _stickyHideTimer?.Stop();
        StickyDateChip.Visibility = Visibility.Collapsed;
    }

    private void UpdateStickyDate()
    {
        var label = ResolveStickyDayLabel();
        if (string.IsNullOrWhiteSpace(label))
        {
            StickyDateChip.Visibility = Visibility.Collapsed;
            return;
        }
        StickyDateText.Text = label;
    }

    private string ResolveStickyDayLabel()
    {
        foreach (var item in MessagesList.Items)
        {
            if (item is not MessageViewModel row) continue;
            var container = MessagesList.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
            if (container == null) continue;
            var transform = container.TransformToAncestor(MessagesList);
            var top = transform.Transform(new System.Windows.Point(0, 0)).Y;
            var bottom = top + container.ActualHeight;
            if (bottom < 0) continue;
            if (top > MessagesList.ActualHeight) break;

            if (!string.IsNullOrEmpty(row.DayLabel))
                return row.DayLabel;
        }

        if (DataContext is not MessengerViewModel vm || vm.Messages.Count == 0)
            return string.Empty;
        foreach (var m in vm.Messages)
        {
            if (!string.IsNullOrEmpty(m.DayLabel))
                return m.DayLabel;
        }
        return string.Empty;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var found = FindScrollViewer(child);
            if (found != null) return found;
        }
        return null;
    }
}
