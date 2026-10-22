using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class MapLocalFolderStringsTests
{
    [Fact]
    public void HubCta_ReturnsEnglishAndRussian()
    {
        Assert.Equal("Add local folder to Cloud…", MapLocalFolderStrings.HubCta("en"));
        Assert.Equal("Добавить локальную папку в облако…", MapLocalFolderStrings.HubCta("ru"));
    }

    [Fact]
    public void NotesNudge_ReturnsDiscoverableText()
    {
        Assert.Contains("Add this folder to Cloud", MapLocalFolderStrings.NotesNudgeText("en-US"));
        Assert.Contains("облаке", MapLocalFolderStrings.NotesNudgeText("ru-RU"), StringComparison.OrdinalIgnoreCase);
    }
}
