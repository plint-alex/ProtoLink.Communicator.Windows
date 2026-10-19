using System.Diagnostics;
using System.IO;

namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>Launch Windows Explorer at a path (select folder when practical).</summary>
public static class WindowsExplorer
{
    /// <summary>Arguments for <c>explorer.exe</c> that select/highlight <paramref name="fullPath"/>.</summary>
    public static string BuildSelectArguments(string fullPath)
    {
        var path = Path.GetFullPath(fullPath);
        return "/select,\"" + path + "\"";
    }

    /// <summary>Open Explorer with the folder selected. Returns false if path missing.</summary>
    public static bool TryOpenFolderSelected(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return false;

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = BuildSelectArguments(folderPath),
            UseShellExecute = true
        });
        return true;
    }
}
