using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class NotesFormatBarStringsTests
{
    [Fact]
    public void Bold_ReturnsEnglishAndRussian()
    {
        Assert.Contains("Bold", NotesFormatBarStrings.Bold("en"));
        Assert.Contains("Ctrl+B", NotesFormatBarStrings.Bold("en"));
        Assert.Contains("Жирный", NotesFormatBarStrings.Bold("ru"));
    }

    [Fact]
    public void InsertLink_ReturnsLocalizedAutomationFriendlyLabels()
    {
        Assert.Equal("Insert link", NotesFormatBarStrings.InsertLink("en-US"));
        Assert.Equal("Вставить ссылку", NotesFormatBarStrings.InsertLink("ru-RU"));
    }
}
