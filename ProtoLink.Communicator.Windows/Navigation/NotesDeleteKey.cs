using System.IO;
using System.Windows.Input;

namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>
/// Policy for Notes tree Delete/Backspace → delete selected page (not editor content).
/// Spec: docs/features/entity-navigation/notes-delete-key (AT-DK-1…4).
/// </summary>
public static class NotesDeleteKey
{
    /// <summary>
    /// True when Delete or Back should run the page-delete pipeline.
    /// Requires Notes tree keyboard focus; never when the editor WebView has focus.
    /// </summary>
    public static bool ShouldHandlePageDeleteKey(Key key, bool treeKeyboardFocused, bool editorKeyboardFocused)
    {
        if (!treeKeyboardFocused || editorKeyboardFocused)
            return false;

        return key is Key.Delete or Key.Back;
    }

    /// <summary>
    /// False for empty path or the notes root folder (same guard as context-menu Delete).
    /// </summary>
    public static bool IsDeletablePagePath(string? folderPath, string? notesRootPath)
    {
        if (string.IsNullOrEmpty(folderPath))
            return false;

        if (string.IsNullOrEmpty(notesRootPath))
            return true;

        try
        {
            return !string.Equals(
                Path.GetFullPath(folderPath),
                Path.GetFullPath(notesRootPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
