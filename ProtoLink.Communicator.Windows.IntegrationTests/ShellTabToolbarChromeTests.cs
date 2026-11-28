using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class ShellTabToolbarChromeTests
{
    [Fact]
    public void PermanentActionIds_Empty_ForMessenger()
    {
        Assert.Empty(ShellTabToolbarChrome.PermanentActionIds(ShellTabToolbarChrome.MessengerTabIndex));
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
    public void CloudTab_HasCarg1ChromeOnly()
    {
        var ids = ShellTabToolbarChrome.PermanentActionIds(ShellTabToolbarChrome.CloudTabIndex);
        Assert.Equal(2, ids.Count);
        Assert.All(ids, id => Assert.True(ShellTabToolbarChrome.IsCloudChromeAction(id)));
        Assert.Contains(ShellTabToolbarChrome.CloudRefreshActionId, ids);
        Assert.Contains(ShellTabToolbarChrome.CloudAddLocalActionId, ids);
        Assert.Equal(new[]
        {
            ShellTabToolbarChrome.CloudRefreshActionId,
            ShellTabToolbarChrome.CloudAddLocalActionId
        }, ids);
    }

    [Fact]
    public void NotesAndCloud_DoNotAllowPermanentCreateOrDownloadOpen()
    {
        Assert.False(ShellTabToolbarChrome.AllowsPermanentCreateActions(ShellTabToolbarChrome.NotesTabIndex));
        Assert.False(ShellTabToolbarChrome.AllowsPermanentCreateActions(ShellTabToolbarChrome.CloudTabIndex));
        Assert.False(ShellTabToolbarChrome.AllowsPermanentDownloadOpenActions(ShellTabToolbarChrome.CloudTabIndex));
    }

    [Fact]
    public void WiredPermanentActions_CoverNotesAndCloudIds()
    {
        Assert.True(ShellTabToolbarChrome.IsWiredPermanentAction(ShellTabToolbarChrome.NotesAddRootActionId));
        Assert.True(ShellTabToolbarChrome.IsWiredPermanentAction(ShellTabToolbarChrome.CloudRefreshActionId));
        Assert.True(ShellTabToolbarChrome.IsWiredPermanentAction(ShellTabToolbarChrome.CloudAddLocalActionId));
        Assert.False(ShellTabToolbarChrome.IsWiredPermanentAction("upload"));
        Assert.False(ShellTabToolbarChrome.IsCloudChromeAction(ShellTabToolbarChrome.NotesAddRootActionId));
    }
}
