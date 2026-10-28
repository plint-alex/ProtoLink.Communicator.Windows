using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class CloudExplorerColumnLayoutTests
{
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void ShouldReserveTreeColumn_MatchesE9dPolicy(bool compact, bool showTree, bool expected)
    {
        Assert.Equal(expected, CloudExplorerColumnLayout.ShouldReserveTreeColumn(compact, showTree));
    }
}
