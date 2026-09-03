using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using ProtoLink.Communicator.Windows.Dialogs;
using ProtoLink.Communicator.Windows.Logging;
using ProtoLink.Communicator.Windows.Services;

namespace ProtoLink.Communicator.Windows;

public partial class App : System.Windows.Application
{
    public static ILoggerFactory LoggerFactory { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Register global exception handlers so that all unhandled exceptions
        // are surfaced to the user with copyable details.
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        ThemeManager.ApplyFromSettingsFile();

        base.OnStartup(e);
        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b
            .AddDebug()
            .AddProvider(new DevToolsLoggerProvider())
            .SetMinimumLevel(LogLevel.Debug));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LoggerFactory?.Dispose();
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var logger = LoggerFactory.CreateLogger<App>();
            logger.LogError(e.Exception, "Unhandled UI exception");

            var text = BuildExceptionText("UNHANDLED UI EXCEPTION", e.Exception);
            ErrorDetailDialog.Show("Error", text);
        }
        catch
        {
            // Best-effort fallback if dialog fails
            System.Windows.MessageBox.Show(
                $"An unhandled UI exception occurred:\n\n{e.Exception}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        // Prevent the application from crashing where possible
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception ex)
            return;

        try
        {
            var logger = LoggerFactory.CreateLogger<App>();
            logger.LogError(ex, "Unhandled AppDomain exception. IsTerminating={IsTerminating}", e.IsTerminating);

            if (ApiExceptionHelper.IndicatesUnauthorizedHttp(ex))
                return;

            var text = BuildExceptionText("UNHANDLED APPDOMAIN EXCEPTION", ex, includeIsTerminating: e.IsTerminating);

            // Ensure we show the dialog on the UI thread if possible
            if (Current?.Dispatcher != null)
            {
                Current.Dispatcher.BeginInvoke(new Action(() => ErrorDetailDialog.Show("Background error", text)));
            }
            else
            {
                System.Windows.MessageBox.Show(
                    $"An unhandled background exception occurred:\n\n{text}",
                    "Background error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch
        {
            System.Windows.MessageBox.Show(
                $"An unhandled background exception occurred:\n\n{ex}",
                "Background error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            var logger = LoggerFactory.CreateLogger<App>();
            logger.LogError(e.Exception, "Unobserved task exception");

            if (ApiExceptionHelper.IndicatesUnauthorizedHttp(e.Exception))
            {
                e.SetObserved();
                return;
            }

            var text = BuildExceptionText("UNOBSERVED TASK EXCEPTION", e.Exception);

            if (Current?.Dispatcher != null)
            {
                Current.Dispatcher.BeginInvoke(new Action(() => ErrorDetailDialog.Show("Background task error", text)));
            }
            else
            {
                System.Windows.MessageBox.Show(
                    $"An unobserved task exception occurred:\n\n{text}",
                    "Background task error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch
        {
            System.Windows.MessageBox.Show(
                $"An unobserved task exception occurred:\n\n{e.Exception}",
                "Background task error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        // Mark as observed so it doesn't crash the process on finalizer
        e.SetObserved();
    }

    private static string BuildExceptionText(string heading, Exception ex, bool includeIsTerminating = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine(heading);
        sb.AppendLine(new string('=', heading.Length));
        sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        sb.AppendLine($"Type: {ex.GetType().FullName}");
        sb.AppendLine($"Message: {ex.Message}");
        if (includeIsTerminating)
        {
            sb.AppendLine($"IsTerminating: {includeIsTerminating}");
        }
        sb.AppendLine();
        sb.AppendLine("Stack trace:");
        sb.AppendLine(ex.StackTrace);

        var inner = ex.InnerException;
        var depth = 1;
        while (inner != null)
        {
            sb.AppendLine();
            sb.AppendLine($"Inner exception #{depth}: {inner.GetType().FullName}");
            sb.AppendLine($"Message: {inner.Message}");
            sb.AppendLine("Stack trace:");
            sb.AppendLine(inner.StackTrace);
            inner = inner.InnerException;
            depth++;
        }

        return sb.ToString();
    }
}

