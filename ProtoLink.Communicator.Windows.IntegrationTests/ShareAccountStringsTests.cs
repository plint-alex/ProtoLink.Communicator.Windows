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
}
