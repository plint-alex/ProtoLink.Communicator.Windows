using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class ApiBaseAddressPathsTests
{
    [Theory]
    [InlineData("https://protolink.ru/api", "https://protolink.ru/api/Version")]
    [InlineData("https://protolink.ru/api/", "https://protolink.ru/api/Version")]
    [InlineData("https://protolink.ru/API", "https://protolink.ru/API/Version")]
    [InlineData("http://localhost:5000/", "http://localhost:5000/api/Version")]
    [InlineData("http://localhost:5000", "http://localhost:5000/api/Version")]
    public void BuildUnderApiRoot_Version_NoDoubleApi(string apiBase, string expected)
    {
        var uri = ApiBaseAddressPaths.BuildUnderApiRoot(apiBase, "Version");
        Assert.Equal(expected, uri.ToString());
    }

    [Fact]
    public void BuildUnderApiRoot_RejectsEmpty()
    {
        Assert.Throws<ArgumentException>(() => ApiBaseAddressPaths.BuildUnderApiRoot(" ", "Version"));
        Assert.Throws<ArgumentException>(() => ApiBaseAddressPaths.BuildUnderApiRoot("https://protolink.ru/api", " "));
    }
}
