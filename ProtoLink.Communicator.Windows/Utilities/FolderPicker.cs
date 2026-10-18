using System.IO;

namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// WPF folder picker. Prefer <see cref="Microsoft.Win32.OpenFolderDialog"/> over
/// <see cref="System.Windows.Forms.FolderBrowserDialog"/> — the WinForms dialog can throw
/// <see cref="FileNotFoundException"/> (empty message) on click in this app.
/// </summary>
internal static class FolderPicker
{
    /// <summary>Shows a folder picker; returns the selected path or null if cancelled.</summary>
    public static string? PickFolder(string title, string? initialPath = null)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            try
            {
                var dir = Directory.Exists(initialPath)
                    ? initialPath
                    : Path.GetDirectoryName(initialPath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    dlg.InitialDirectory = dir;
            }
            catch
            {
                // ignore bad initial path
            }
        }

        return dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName)
            ? dlg.FolderName
            : null;
    }
}
