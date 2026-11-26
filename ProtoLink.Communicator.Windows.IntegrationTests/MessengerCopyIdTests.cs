using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Messenger message Copy id (M13) — label + can-execute gate (offline; no clipboard).</summary>
[Trait("Category", "Offline")]
public class MessengerCopyIdTests
{
    [Fact]
    public void CopyIdLabel_MatchesNotesCloudWording_NotContactLabel()
    {
        Assert.Equal("Copy id", ShareAccountStrings.CopyIdLabel("en"));
        Assert.Equal("Копировать id", ShareAccountStrings.CopyIdLabel("ru"));
        Assert.NotEqual(
            ShareAccountStrings.CopyContactIdLabel("en"),
            ShareAccountStrings.CopyIdLabel("en"));
        Assert.NotEqual(
            ShareAccountStrings.CopyContactIdLabel("ru"),
            ShareAccountStrings.CopyIdLabel("ru"));

        using var http = new HttpClient { BaseAddress = new Uri("http://localhost/") };
        var vm = new MessengerViewModel(new StubAuth(), http);
        Assert.Equal(ShareAccountStrings.CopyIdLabel(null), vm.CopyIdLabel);
    }

    [Fact]
    public void CopyIdCommand_CanExecute_RequiresLastingMessageEntityId()
    {
        using var http = new HttpClient { BaseAddress = new Uri("http://localhost/") };
        var vm = new MessengerViewModel(new StubAuth(), http);

        Assert.False(vm.CopyIdCommand.CanExecute(null));

        var pending = new MessageViewModel { EntityId = Guid.Empty, Text = "optimistic" };
        Assert.False(vm.CopyIdCommand.CanExecute(pending));
        Assert.False(MessengerViewModel.CanCopyMessageId(pending));

        var dateChip = MessageViewModel.DateHeader("Today");
        Assert.False(vm.CopyIdCommand.CanExecute(dateChip));

        var id = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var lasting = new MessageViewModel { EntityId = id, Text = "hi" };
        Assert.True(vm.CopyIdCommand.CanExecute(lasting));
        Assert.Equal("a1b2c3d4-e5f6-7890-abcd-ef1234567890",
            ShareAccountStrings.FormatEntityIdForClipboard(lasting.EntityId));
    }

    private sealed class StubAuth : IAuthService
    {
        public bool IsAuthenticated => false;
        public TokenData? CurrentToken => null;

        public Task<LoginResult> LoginAsync(string login, string password) =>
            throw new NotSupportedException();

        public Task<RegisterResult> RegisterAsync(string email, string password, string lang) =>
            throw new NotSupportedException();

        public Task<ChangePasswordResult> ChangePasswordAsync(string currentPassword, string newPassword) =>
            throw new NotSupportedException();

        public Task<bool> RefreshTokenAsync() => Task.FromResult(false);

        public void Logout() { }
    }
}
