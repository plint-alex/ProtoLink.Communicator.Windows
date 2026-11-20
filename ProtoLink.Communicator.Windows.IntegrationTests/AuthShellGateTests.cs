using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Offline coverage for Notes-without-login shell gate (#notes-without-login).
/// </summary>
[Trait("Category", "Offline")]
public class AuthShellGateTests
{
    [Fact]
    public void HideTabsUntilAuthenticated_IsFalse()
    {
        Assert.False(AuthShellGate.HideTabsUntilAuthenticated);
    }

    [Theory]
    [InlineData(AuthShellGate.MessengerTabIndex, true)]
    [InlineData(AuthShellGate.CloudTabIndex, true)]
    [InlineData(AuthShellGate.NotesTabIndex, false)]
    public void TabRequiresLogin_OnlyMessengerAndCloud(int tab, bool expected)
    {
        Assert.Equal(expected, AuthShellGate.TabRequiresLogin(tab));
    }

    [Fact]
    public void ShouldPromptLoginForTab_WhenUnsignedMessengerOrCloud()
    {
        Assert.True(AuthShellGate.ShouldPromptLoginForTab(false, AuthShellGate.MessengerTabIndex));
        Assert.True(AuthShellGate.ShouldPromptLoginForTab(false, AuthShellGate.CloudTabIndex));
        Assert.False(AuthShellGate.ShouldPromptLoginForTab(false, AuthShellGate.NotesTabIndex));
        Assert.False(AuthShellGate.ShouldPromptLoginForTab(true, AuthShellGate.MessengerTabIndex));
        Assert.False(AuthShellGate.ShouldPromptLoginForTab(true, AuthShellGate.CloudTabIndex));
    }

    [Fact]
    public void ResolveTabAfterAuthChange_BouncesLoginTabsToNotesWhenUnsigned()
    {
        Assert.Equal(
            AuthShellGate.NotesTabIndex,
            AuthShellGate.ResolveTabAfterAuthChange(false, AuthShellGate.MessengerTabIndex));
        Assert.Equal(
            AuthShellGate.NotesTabIndex,
            AuthShellGate.ResolveTabAfterAuthChange(false, AuthShellGate.CloudTabIndex));
        Assert.Equal(
            AuthShellGate.NotesTabIndex,
            AuthShellGate.ResolveTabAfterAuthChange(false, AuthShellGate.NotesTabIndex));
        Assert.Equal(
            AuthShellGate.MessengerTabIndex,
            AuthShellGate.ResolveTabAfterAuthChange(true, AuthShellGate.MessengerTabIndex));
    }

    [Fact]
    public void UnsignedDefaultTab_IsNotes()
    {
        Assert.Equal(AuthShellGate.NotesTabIndex, AuthShellGate.UnsignedDefaultTabIndex);
    }

    [Fact]
    public void SignedInDefaultTab_IsMessenger()
    {
        Assert.Equal(AuthShellGate.MessengerTabIndex, AuthShellGate.SignedInDefaultTabIndex);
    }

    [Theory]
    [InlineData(true, AuthShellGate.MessengerTabIndex)]
    [InlineData(false, AuthShellGate.NotesTabIndex)]
    public void ResolveInitialTab_SignedInMessenger_UnsignedNotes(bool authenticated, int expected)
    {
        Assert.Equal(expected, AuthShellGate.ResolveInitialTab(authenticated));
    }

    [Fact]
    public void ResolveTabAfterAuthChange_KeepsNotesWhenSignedIn()
    {
        // Intentional Notes while signed-in must not bounce to Messenger on UpdateAuthGate.
        Assert.Equal(
            AuthShellGate.NotesTabIndex,
            AuthShellGate.ResolveTabAfterAuthChange(true, AuthShellGate.NotesTabIndex));
    }
}
