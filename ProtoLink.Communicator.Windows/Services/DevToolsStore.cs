using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace ProtoLink.Communicator.Windows.Services;

public sealed class NetworkLogEntry
{
    public DateTime Timestamp { get; init; }
    public string Method { get; init; } = "";
    public string Url { get; init; } = "";
    public int? StatusCode { get; init; }
    public long DurationMs { get; init; }
    public string? Error { get; init; }
    /// <summary>Captured request body (UTF-8), may be truncated for very large payloads.</summary>
    public string? RequestPayload { get; init; }
    /// <summary>Captured response body (UTF-8), may be truncated.</summary>
    public string? ResponseBody { get; init; }
    /// <summary>Request line + request + content headers (text).</summary>
    public string? RequestHeaders { get; init; }
    /// <summary>Status line + response + content headers (text).</summary>
    public string? ResponseHeaders { get; init; }
}

/// <summary>
/// Shared devtools state (console + HTTP). All mutations marshal to the UI thread.
/// </summary>
public static class DevToolsStore
{
    private const int MaxConsoleLines = 2500;
    private const int MaxNetworkEntries = 800;

    public static ObservableCollection<string> ConsoleLines { get; } = new();
    public static ObservableCollection<NetworkLogEntry> NetworkEntries { get; } = new();

    public static void AppendConsole(string line)
    {
        var text = $"{DateTime.Now:HH:mm:ss.fff}  {line}";
        PostUi(() =>
        {
            ConsoleLines.Add(text);
            TrimHead(ConsoleLines, MaxConsoleLines);
        });
    }

    public static void AppendNetwork(
        string method,
        string url,
        int? statusCode,
        long durationMs,
        string? requestPayload,
        string? responseBody,
        string? requestHeaders,
        string? responseHeaders,
        string? error = null)
    {
        PostUi(() =>
        {
            NetworkEntries.Add(new NetworkLogEntry
            {
                Timestamp = DateTime.Now,
                Method = method,
                Url = url,
                StatusCode = statusCode,
                DurationMs = durationMs,
                Error = error,
                RequestPayload = requestPayload,
                ResponseBody = responseBody,
                RequestHeaders = requestHeaders,
                ResponseHeaders = responseHeaders
            });
            TrimHead(NetworkEntries, MaxNetworkEntries);
        });
    }

    public static void ClearConsole()
    {
        PostUi(ConsoleLines.Clear);
    }

    public static void ClearNetwork()
    {
        PostUi(NetworkEntries.Clear);
    }

    private static void PostUi(Action action)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d == null)
        {
            action();
            return;
        }

        if (d.CheckAccess())
            action();
        else
            d.BeginInvoke(action, DispatcherPriority.Normal);
    }

    private static void TrimHead<T>(ObservableCollection<T> col, int max)
    {
        while (col.Count > max)
            col.RemoveAt(0);
    }
}
