using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class CloudCreateFabStringsTests
{
    [Fact]
    public void FabName_ReturnsEnglishAndRussian()
    {
        Assert.Equal("Create", CloudCreateFabStrings.FabName("en"));
        Assert.Equal("Создать", CloudCreateFabStrings.FabName("ru"));
    }

    [Fact]
    public void MenuLabels_ReturnLocalizedText()
    {
        Assert.Equal("New folder", CloudCreateFabStrings.NewFolder("en-US"));
        Assert.Equal("Upload file", CloudCreateFabStrings.UploadFile("en-US"));
        Assert.Equal("Новая папка", CloudCreateFabStrings.NewFolder("ru-RU"));
        Assert.Equal("Загрузить файл", CloudCreateFabStrings.UploadFile("ru-RU"));
    }
}
