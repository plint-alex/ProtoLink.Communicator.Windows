using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ProtoLink.Communicator.Windows.Services;

namespace ProtoLink.Communicator.Windows.Logging;

/// <summary>
/// Forwards Microsoft.Extensions.Logging to the devtools Console tab.
/// </summary>
public sealed class DevToolsLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, DevToolsLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, static name => new DevToolsLogger(name));

    public void Dispose() => _loggers.Clear();
}

internal sealed class DevToolsLogger : ILogger
{
    private readonly string _category;

    public DevToolsLogger(string category) => _category = category;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var msg = formatter(state, exception);
        var line = $"[{logLevel}] {_category}: {msg}";
        if (exception != null)
            line += Environment.NewLine + exception;
        DevToolsStore.AppendConsole(line);
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
