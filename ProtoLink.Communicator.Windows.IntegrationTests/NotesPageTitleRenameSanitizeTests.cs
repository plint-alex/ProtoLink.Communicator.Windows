using ProtoLink.Communicator.Windows.Services.Notes;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Offline: H1 → folder name keeps spaces; only invalid filename chars become '_'.
/// </summary>
[Trait("Category", "Offline")]
public class NotesPageTitleRenameSanitizeTests
{
    [Fact]
    public void Sanitize_KeepsSpaces()
    {
        Assert.Equal("My note title", NotesPageTitleRenameService.SanitizeFolderNameFromTitle("My note title"));
    }

    [Fact]
    public void Sanitize_ReplacesInvalidChars_WithoutSpaceToUnderscore()
    {
        var name = NotesPageTitleRenameService.SanitizeFolderNameFromTitle("My note: title?");
        Assert.Equal("My note_ title", name);
        Assert.Contains(' ', name);
        Assert.DoesNotContain("My_note", name);
    }

    [Fact]
    public void Sanitize_TrimsAndEmptySkips()
    {
        Assert.Equal("Hello world", NotesPageTitleRenameService.SanitizeFolderNameFromTitle("  Hello world  "));
        Assert.Equal(string.Empty, NotesPageTitleRenameService.SanitizeFolderNameFromTitle("   "));
        Assert.Equal(string.Empty, NotesPageTitleRenameService.SanitizeFolderNameFromTitle(""));
    }
}
