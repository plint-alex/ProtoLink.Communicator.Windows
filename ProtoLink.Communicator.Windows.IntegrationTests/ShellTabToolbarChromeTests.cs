using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class ShellTabToolbarChromeTests
{
    [Theory]
    [InlineData(ShellTabToolbarChrome.MessengerTabIndex)]
    [InlineData(ShellTabToolbarChrome.CloudTabIndex)]
    public void PermanentActionIds_Empty_ForMessengerAndCloud(int tabIndex)
    {
        Assert.Empty(ShellTabToolbarChrome.PermanentActionIds(tabIndex));
    }

    [Fact]
    public void NotesTab_HasE11aRootChromeOnly()
    {
        var ids = ShellTabToolbarChrome.PermanentActionIds(ShellTabToolbarChrome.NotesTabIndex);
        Assert.Equal(3, ids.Count);
        Assert.All(ids, id => Assert.True(ShellTabToolbarChrome.IsNotesRootChromeAction(id)));
        Assert.Contains(ShellTabToolbarChrome.NotesAddRootActionId, ids);
        Assert.Contains(ShellTabToolbarChrome.NotesRemoveRootActionId, ids);
        Assert.Contains(ShellTabToolbarChrome.NotesMapRootActionId, ids);
    }

    [Fact]
    public void NotesAndCloud_DoNotAllowPermanentCreateOrDownloadOpen()
    {
        Assert.False(ShellTabToolbarChrome.AllowsPermanentCreateActions(ShellTabToolbarChrome.NotesTabIndex));
        Assert.False(ShellTabToolbarChrome.AllowsPermanentCreateActions(ShellTabToolbarChrome.CloudTabIndex));
        Assert.False(ShellTabToolbarChrome.AllowsPermanentDownloadOpenActions(ShellTabToolbarChrome.CloudTabIndex));
    }
}
