using System.Net.Http;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels.Share;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>UX-S9 / AT-S41 offline gating on Share sheet mutations.</summary>
[Trait("Category", "Offline")]
public class ShareSheetViewModelOfflineTests
{
    [Fact]
    public async Task Offline_DisablesMutations_ButSheetCanOpen()
    {
        var api = new CloudApiService(new HttpClient { BaseAddress = new Uri("https://example.invalid/") });
        var vm = new ShareSheetViewModel(api, () => "en-US");
        await vm.OpenAsync(Guid.NewGuid(), "Demo", isFolder: true, childCount: 2, canEdit: true, isOnline: false);

        Assert.True(vm.IsOpen);
        Assert.True(vm.ShowOfflineBanner);
        Assert.False(vm.CanMutate);
        Assert.False(vm.BeginCreateCommand.CanExecute(null));
        Assert.True(vm.CopyCanonicalLinkCommand.CanExecute(null) || vm.CopyCanonicalLink == null);

        vm.CopyCanonicalLink = () => { };
        Assert.True(vm.CopyCanonicalLinkCommand.CanExecute(null));
    }

    [Fact]
    public async Task OnlineEditor_CanBeginCreate()
    {
        var api = new CloudApiService(new HttpClient { BaseAddress = new Uri("https://example.invalid/") });
        var vm = new ShareSheetViewModel(api, () => "en-US");
        await vm.OpenAsync(Guid.NewGuid(), "Demo", isFolder: false, childCount: 0, canEdit: true, isOnline: true);

        Assert.True(vm.CanMutate);
        Assert.True(vm.ShowCreateButton);
        Assert.True(vm.BeginCreateCommand.CanExecute(null));
        Assert.False(vm.ShowSubtreeLine);
    }

    [Fact]
    public async Task Viewer_ShowsFooter_AndCannotMutate()
    {
        var api = new CloudApiService(new HttpClient { BaseAddress = new Uri("https://example.invalid/") });
        var vm = new ShareSheetViewModel(api, () => "ru-RU");
        await vm.OpenAsync(Guid.NewGuid(), "Папка", isFolder: true, childCount: 1, canEdit: false, isOnline: true);

        Assert.False(vm.CanMutate);
        Assert.True(vm.ShowViewerFooter);
        Assert.Contains("редакт", vm.ViewerFooterText, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task Offline_DisablesAddPeople_ButCopyLinkWorks()
    {
        var api = new CloudApiService(new HttpClient { BaseAddress = new Uri("https://example.invalid/") });
        var vm = new ShareSheetViewModel(api, () => "en-US", currentUserId: () => Guid.NewGuid());
        await vm.OpenAsync(Guid.NewGuid(), "Demo", isFolder: true, childCount: 1, canEdit: true, isOnline: false);

        Assert.False(vm.CanMutate);
        Assert.False(vm.BeginAddPeopleCommand.CanExecute(null));
        Assert.False(vm.AddPeopleCommand.CanExecute(null));

        vm.CopyCanonicalLink = () => { };
        Assert.True(vm.CopyCanonicalLinkCommand.CanExecute(null));
        Assert.True(vm.CopyCanonicalFromAddedCommand.CanExecute(null));
    }
}