using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Messenger Copy contact id — format gate (offline; no clipboard).</summary>
[Trait("Category", "Offline")]
public class MessengerCopyContactIdTests
{
    [Fact]
    public void CopyContactIdCommand_CanExecute_RequiresResolvedUserId()
    {
        using var http = new HttpClient { BaseAddress = new Uri("http://localhost/") };
        var vm = new MessengerViewModel(new StubAuth(), http);
        Assert.False(vm.CopyContactIdCommand.CanExecute(null));

        var empty = new Contact { Id = Guid.Empty, Name = "x" };
        Assert.False(vm.CopyContactIdCommand.CanExecute(empty));

        var ok = new Contact { Id = Guid.Parse("8f3a1111-2222-3333-4444-555555555555"), Name = "Ada" };
        Assert.True(vm.CopyContactIdCommand.CanExecute(ok));
        Assert.Equal(
            "8f3a1111-2222-3333-4444-555555555555",
            ShareAccountStrings.FormatUserIdForClipboard(ok.Id));
    }

    private sealed class StubAuth : IAuthService
    {
        public bool IsAuthenticated => false;
        public TokenData? CurrentToken => null;

        public Task<LoginResult> LoginAsync(string login, string password) =>
            throw new NotSupportedException();

        public Task<RegisterResult> RegisterAsync(string email, string password, string lang) =>
            throw new NotSupportedException();

        public Task<bool> RefreshTokenAsync() => Task.FromResult(false);

        public void Logout() { }
    }
}
