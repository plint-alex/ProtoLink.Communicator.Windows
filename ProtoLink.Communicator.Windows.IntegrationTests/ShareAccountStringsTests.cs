using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Share-sheet-ux account strings parity with web Layout defaults.</summary>
[Trait("Category", "Offline")]
public class ShareAccountStringsTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("en-US", false)]
    [InlineData("ru-RU", true)]
    [InlineData("ru", true)]
    public void IsRussian_MatchesPrefix(string? lang, bool expected) =>
        Assert.Equal(expected, ShareAccountStrings.IsRussian(lang));

    [Fact]
    public void CopyMyId_EnglishDefaults()
    {
        Assert.Equal("Copy my ID", ShareAccountStrings.CopyMyIdLabel("en-US"));
        Assert.Equal(
            "Share this with people who want to give you access",
            ShareAccountStrings.CopyMyIdHint(null));
        Assert.Equal("Copied", ShareAccountStrings.CopiedLabel("en"));
    }

    [Fact]
    public void CopyMyId_RussianDefaults()
    {
        Assert.Equal("Скопировать мой ID", ShareAccountStrings.CopyMyIdLabel("ru-RU"));
        Assert.Equal(
            "Отправьте это тем, кто хочет дать вам доступ",
            ShareAccountStrings.CopyMyIdHint("ru"));
        Assert.Equal("Скопировано", ShareAccountStrings.CopiedLabel("ru-RU"));
    }

    [Fact]
    public void CopyContactId_EnglishAndRussian()
    {
        Assert.Equal("Copy contact id", ShareAccountStrings.CopyContactIdLabel("en-US"));
        Assert.Equal("Скопировать ID контакта", ShareAccountStrings.CopyContactIdLabel("ru-RU"));
    }

    [Fact]
    public void CopyId_EnglishAndRussian()
    {
        Assert.Equal("Copy id", ShareAccountStrings.CopyIdLabel("en-US"));
        Assert.Equal("Копировать id", ShareAccountStrings.CopyIdLabel("ru-RU"));
    }

    [Fact]
    public void FormatUserIdForClipboard_DFormatOrNull()
    {
        var id = Guid.Parse("8f3a1111-2222-3333-4444-555555555555");
        Assert.Equal("8f3a1111-2222-3333-4444-555555555555", ShareAccountStrings.FormatUserIdForClipboard(id));
        Assert.Null(ShareAccountStrings.FormatUserIdForClipboard(Guid.Empty));
        Assert.Equal(
            "8f3a1111-2222-3333-4444-555555555555",
            ShareAccountStrings.FormatUserIdForClipboard("8F3A1111-2222-3333-4444-555555555555"));
        Assert.Null(ShareAccountStrings.FormatUserIdForClipboard(""));
        Assert.Null(ShareAccountStrings.FormatUserIdForClipboard("not-a-guid"));
        Assert.Equal(
            ShareAccountStrings.FormatUserIdForClipboard(id),
            ShareAccountStrings.FormatEntityIdForClipboard(id));
        Assert.Null(ShareAccountStrings.FormatEntityIdForClipboard(Guid.Empty));
    }
}
