using System.IO;
using System.Text.RegularExpressions;
using System.Net;

namespace ProtoLink.Communicator.Windows.Services.Notes;

public sealed class NotesPageTitleRenameService
{
    private static readonly Regex H1Regex = new(@"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private readonly NotesFileService _fileService;
    private readonly NotesFileSystemService _fs;

    public NotesPageTitleRenameService(NotesFileService fileService, NotesFileSystemService fs)
    {
        _fileService = fileService;
        _fs = fs;
    }

    /// <summary>
    /// If the first H1 text implies a new folder name and the folder can be moved, renames the page folder and persists HTML to the index file.
    /// Returns the folder path to use for subsequent saves (unchanged if no rename).
    /// </summary>
    public async Task<string> TryRenameFromFirstH1Async(string? oldHtml, string newHtml, string currentPageFolderPath)
    {
        var titleMatch = H1Regex.Match(newHtml);
        if (!titleMatch.Success)
            return currentPageFolderPath;

        var newTitleHtml = titleMatch.Groups[1].Value;
        var newTitle = WebUtility.HtmlDecode(newTitleHtml).Trim();
        if (string.IsNullOrEmpty(newTitle))
            return currentPageFolderPath;

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitizedTitle = string.Join("_", newTitle.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .Replace(" ", "_");
        if (string.IsNullOrEmpty(sanitizedTitle))
            return currentPageFolderPath;

        string? oldTitle = null;
        if (!string.IsNullOrEmpty(oldHtml))
        {
            var oldMatch = H1Regex.Match(oldHtml);
            if (oldMatch.Success)
            {
                var oldTitleHtml = oldMatch.Groups[1].Value;
                oldTitle = WebUtility.HtmlDecode(oldTitleHtml).Trim();
            }
        }

        if (oldTitle == newTitle)
            return currentPageFolderPath;

        if (!Directory.Exists(currentPageFolderPath))
            return currentPageFolderPath;

        var parentDir = Path.GetDirectoryName(currentPageFolderPath);
        if (string.IsNullOrEmpty(parentDir))
            return currentPageFolderPath;

        var newPagePath = Path.Combine(parentDir, sanitizedTitle);
        if (string.Equals(newPagePath, currentPageFolderPath, StringComparison.OrdinalIgnoreCase))
            return currentPageFolderPath;
        if (Directory.Exists(newPagePath))
            return currentPageFolderPath;

        Directory.Move(currentPageFolderPath, newPagePath);
        var indexPath = _fs.GetWriteIndexFilePath(newPagePath);
        await _fileService.SaveFileAsync(indexPath, newHtml);
        return newPagePath;
    }
}
