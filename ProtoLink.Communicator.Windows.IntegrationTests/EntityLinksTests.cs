using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>AT-C4 / AT-X1: entity link format.</summary>
[Trait("Category", "Offline")]
public class EntityLinksTests
{
    private static readonly Guid Id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    [Theory]
    [InlineData("https://protolink.ru/")]
    [InlineData("https://protolink.ru")]
    [InlineData(" https://protolink.ru/ ")]
    public void NoLanguage_IsBaseSlashGuidD(string baseUrl)
    {
        Assert.Equal("https://protolink.ru/0f8fad5b-d9cb-469f-a165-70867728950e", EntityLinks.Build(baseUrl, Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MissingBase_UsesDefaultSite(string? baseUrl)
    {
        Assert.Equal("https://protolink.ru/0f8fad5b-d9cb-469f-a165-70867728950e", EntityLinks.Build(baseUrl, Id));
    }

    [Fact]
    public void LanguageSet_AppendsLangQuery()
    {
        Assert.Equal(
            "https://protolink.ru/0f8fad5b-d9cb-469f-a165-70867728950e?lang=ru-RU",
            EntityLinks.Build("https://protolink.ru/", Id, "ru-RU"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void LanguageNotSet_NoQuery(string? lang)
    {
        Assert.DoesNotContain("?", EntityLinks.Build("https://protolink.ru/", Id, lang));
    }
}
