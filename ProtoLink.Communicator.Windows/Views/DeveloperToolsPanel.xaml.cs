using System.Windows;
using System.Windows.Controls;
using ProtoLink.Communicator.Windows.Services;

namespace ProtoLink.Communicator.Windows.Views;

public partial class DeveloperToolsPanel : System.Windows.Controls.UserControl
{
    public event Action? CloseRequested;

    public DeveloperToolsPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ClearConsoleButton.Click += (_, _) => DevToolsStore.ClearConsole();
        ClearNetworkButton.Click += OnClearNetworkClicked;
        CloseButton.Click += (_, _) => CloseRequested?.Invoke();
        NetworkGrid.SelectionChanged += OnNetworkSelectionChanged;
    }

    private void OnClearNetworkClicked(object sender, RoutedEventArgs e)
    {
        DevToolsStore.ClearNetwork();
        RequestPayloadBox.Text = "";
        ResponseBodyBox.Text = "";
        RequestHeadersBox.Text = "";
        ResponseHeadersBox.Text = "";
    }

    private void OnNetworkSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NetworkGrid.SelectedItem is NetworkLogEntry entry)
        {
            RequestPayloadBox.Text = string.IsNullOrEmpty(entry.RequestPayload)
                ? "(no body)"
                : entry.RequestPayload;
            if (!string.IsNullOrEmpty(entry.ResponseBody))
                ResponseBodyBox.Text = entry.ResponseBody;
            else if (!string.IsNullOrEmpty(entry.Error))
                ResponseBodyBox.Text = "[Error] " + entry.Error;
            else
                ResponseBodyBox.Text = "(no body)";

            RequestHeadersBox.Text = string.IsNullOrEmpty(entry.RequestHeaders)
                ? "(none)"
                : entry.RequestHeaders;
            ResponseHeadersBox.Text = string.IsNullOrEmpty(entry.ResponseHeaders)
                ? (string.IsNullOrEmpty(entry.Error) ? "(none)" : "(no response — see Response body)")
                : entry.ResponseHeaders;
        }
        else
        {
            RequestPayloadBox.Text = "";
            ResponseBodyBox.Text = "";
            RequestHeadersBox.Text = "";
            ResponseHeadersBox.Text = "";
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ConsoleList.ItemsSource = DevToolsStore.ConsoleLines;
        NetworkGrid.ItemsSource = DevToolsStore.NetworkEntries;
        DevToolsStore.ConsoleLines.CollectionChanged += OnConsoleChanged;
        ScrollConsoleToEnd();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DevToolsStore.ConsoleLines.CollectionChanged -= OnConsoleChanged;
        NetworkGrid.SelectionChanged -= OnNetworkSelectionChanged;
    }

    private void OnConsoleChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(ScrollConsoleToEnd), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ScrollConsoleToEnd()
    {
        if (ConsoleList.Items.Count > 0)
            ConsoleList.ScrollIntoView(ConsoleList.Items[^1]);
    }
}
