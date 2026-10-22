using ProtoLink.Communicator.Windows.Models.Share;
using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class NotesTreeShareTests
{
    private static readonly Guid Me = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void HasExternalShare_OnlySelf_False()
    {
        var grants = new[]
        {
            new ShareGrantResult { PrincipalKind = "user", PermissionForId = Me, CanWrite = true }
        };
        Assert.False(NotesTreeShare.HasExternalShare(grants, liveGuestLinkCount: 0, Me));
    }

    [Fact]
    public void HasExternalShare_OtherUser_True()
    {
        var grants = new[]
        {
            new ShareGrantResult { PrincipalKind = "user", PermissionForId = Me, CanWrite = true },
            new ShareGrantResult { PrincipalKind = "user", PermissionForId = Other, CanWrite = false }
        };
        Assert.True(NotesTreeShare.HasExternalShare(grants, liveGuestLinkCount: 0, Me));
    }

    [Fact]
    public void HasExternalShare_LiveGuestLink_True()
    {
        Assert.True(NotesTreeShare.HasExternalShare(
            Array.Empty<ShareGrantResult>(),
            liveGuestLinkCount: 1,
            Me));
    }

    [Fact]
    public void HasExternalShare_PublicGrant_True()
    {
        var grants = new[]
        {
            new ShareGrantResult { PrincipalKind = "user", PermissionForId = Me, CanWrite = true },
            new ShareGrantResult { PrincipalKind = "public", PermissionForId = Guid.Empty, CanWrite = false }
        };
        Assert.True(NotesTreeShare.HasExternalShare(grants, liveGuestLinkCount: 0, Me));
    }

    [Fact]
    public void HasExternalShare_GuestLinkPrincipalIgnoredInPeople_OnlyLiveCounts()
    {
        var grants = new[]
        {
            new ShareGrantResult { PrincipalKind = "user", PermissionForId = Me, CanWrite = true },
            new ShareGrantResult { PrincipalKind = "guest_link", PermissionForId = Guid.NewGuid() }
        };
        Assert.False(NotesTreeShare.HasExternalShare(grants, liveGuestLinkCount: 0, Me));
        Assert.True(NotesTreeShare.HasExternalShare(grants, liveGuestLinkCount: 1, Me));
    }

    [Fact]
    public void HasExternalShare_UnknownMe_TwoUsers_True()
    {
        var grants = new[]
        {
            new ShareGrantResult { PrincipalKind = "user", PermissionForId = Me },
            new ShareGrantResult { PrincipalKind = "user", PermissionForId = Other }
        };
        Assert.True(NotesTreeShare.HasExternalShare(grants, liveGuestLinkCount: 0, currentUserId: null));
        Assert.False(NotesTreeShare.HasExternalShare(
            new[] { new ShareGrantResult { PrincipalKind = "user", PermissionForId = Me } },
            liveGuestLinkCount: 0,
            currentUserId: null));
    }
}
