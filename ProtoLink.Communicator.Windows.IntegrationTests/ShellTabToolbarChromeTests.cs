using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class ShellTabToolbarChromeTests
{
    [Theory]
    [InlineData(ShellTabToolbarChrome.MessengerTabIndex)]
    [InlineData(ShellTabToolbarChrome.NotesTabIndex)]
    [InlineData(ShellTabToolbarChrome.CloudTabIndex)]
    public void PermanentActionIds_Empty_ForAllShellTabs(int tabIndex)
    {
        Assert.Empty(ShellTabToolbarChrome.PermanentActionIds(tabIndex));
    }

    [Fact]
    public void NotesAndCloud_DoNotAllowPermanentCreateOrDownloadOpen()
    {
        Assert.False(ShellTabToolbarChrome.AllowsPermanentCreateActions(ShellTabToolbarChrome.NotesTabIndex));
        Assert.False(ShellTabToolbarChrome.AllowsPermanentCreateActions(ShellTabToolbarChrome.CloudTabIndex));
        Assert.False(ShellTabToolbarChrome.AllowsPermanentDownloadOpenActions(ShellTabToolbarChrome.CloudTabIndex));
    }
}
