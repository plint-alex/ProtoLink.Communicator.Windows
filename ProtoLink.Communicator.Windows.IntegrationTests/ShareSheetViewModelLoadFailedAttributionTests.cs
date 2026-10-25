using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels.Share;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// P2-20261025: LoadFailed is only for List/ListGrants; contacts soft-fail; Add uses AddFailed.
/// </summary>
[Trait("Category", "Offline")]
public class ShareSheetViewModelLoadFailedAttributionTests
{
    private static readonly Guid Me = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid Target = Guid.Parse("8f3a1111-2222-3333-4444-555555555555");
    private static readonly Guid EntityId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public async Task LoadAsync_ListFailure_SetsLoadFailed_AndClearsLists()
    {
        var handler = new AttributionHandler(Mode.FailShareList);
        var statuses = new List<string>();
        var vm = CreateVm(handler, statuses);

        await vm.OpenAsync(EntityId, "Demo", isFolder: true, childCount: 0, canEdit: true, isOnline: true);

        Assert.Equal(ShareSheetStrings.LoadFailed("en-US"), vm.ErrorText);
        Assert.True(vm.HasError);
        Assert.Empty(vm.LiveLinks);
        Assert.Empty(vm.InactiveLinks);
        Assert.Empty(vm.People);
        Assert.Contains(statuses, s => s.StartsWith(ShareSheetStrings.LoadFailed("en-US"), StringComparison.Ordinal));
        Assert.True(vm.RetryLoadCommand.CanExecute(null));
    }

    [Fact]
    public async Task LoadContacts_Failure_DoesNotSetLoadFailed_OverSuccessfulSheet()
    {
        var handler = new AttributionHandler(Mode.FailContacts);
        var statuses = new List<string>();
        var vm = CreateVm(handler, statuses);

        await vm.OpenAsync(EntityId, "Demo", isFolder: true, childCount: 0, canEdit: true, isOnline: true);
        Assert.False(vm.HasError);
        Assert.Null(vm.ErrorText);

        Assert.True(vm.BeginAddPeopleCommand.CanExecute(null));
        vm.BeginAddPeopleCommand.Execute(null);
        await WaitForAsync(() => vm.ShowAddPeople, TimeSpan.FromSeconds(2));
        await Task.Delay(80);

        Assert.False(vm.HasError);
        Assert.Null(vm.ErrorText);
        Assert.Empty(vm.ContactPickerItems);
        Assert.Contains(statuses, s => s.StartsWith(ShareSheetStrings.ContactsLoadSoft("en-US"), StringComparison.Ordinal));
        Assert.DoesNotContain(statuses, s => s.StartsWith(ShareSheetStrings.LoadFailed("en-US"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddPeopleAsync_Failure_UsesAddFailed_NotLoadFailed()
    {
        var handler = new AttributionHandler(Mode.FailAddPermission);
        var statuses = new List<string>();
        var vm = CreateVm(handler, statuses);

        await vm.OpenAsync(EntityId, "Demo", isFolder: true, childCount: 0, canEdit: true, isOnline: true);
        Assert.Null(vm.ErrorText);

        Assert.True(vm.BeginAddPeopleCommand.CanExecute(null));
        vm.BeginAddPeopleCommand.Execute(null);
        await WaitForAsync(() => vm.ShowAddPeople, TimeSpan.FromSeconds(2));
        await Task.Delay(50);

        vm.PasteId = Target.ToString("D");
        await WaitForAsync(() => vm.HasPastePreview, TimeSpan.FromSeconds(2));
        Assert.True(vm.AddPeopleCommand.CanExecute(null));

        vm.AddPeopleCommand.Execute(null);
        await WaitForAsync(() => vm.HasError, TimeSpan.FromSeconds(2));

        Assert.Equal(ShareSheetStrings.AddFailed("en-US"), vm.ErrorText);
        Assert.NotEqual(ShareSheetStrings.LoadFailed("en-US"), vm.ErrorText);
        Assert.Contains(statuses, s => s == ShareSheetStrings.AddFailed("en-US"));
    }

    [Fact]
    public void Strings_AddAndContacts_DistinctFromLoadFailed()
    {
        Assert.Equal("Couldn't add this. Try again.", ShareSheetStrings.AddFailed("en"));
        Assert.Equal("Не удалось добавить. Повторите.", ShareSheetStrings.AddFailed("ru"));
        Assert.Equal("Couldn't load contacts.", ShareSheetStrings.ContactsLoadSoft("en"));
        Assert.Equal("Не удалось загрузить контакты.", ShareSheetStrings.ContactsLoadSoft("ru"));
        Assert.NotEqual(ShareSheetStrings.LoadFailed("en"), ShareSheetStrings.AddFailed("en"));
        Assert.NotEqual(ShareSheetStrings.LoadFailed("en"), ShareSheetStrings.ContactsLoadSoft("en"));
    }

    private static ShareSheetViewModel CreateVm(AttributionHandler handler, List<string> statuses)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var api = new CloudApiService(http);
        return new ShareSheetViewModel(
            api,
            () => "en-US",
            currentUserId: () => Me,
            onStatus: s => statuses.Add(s),
            pasteResolveDebounce: TimeSpan.Zero);
    }

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if (DateTime.UtcNow - start > timeout)
                throw new TimeoutException("Condition not met within " + timeout);
            await Task.Delay(20);
        }
    }

    private enum Mode
    {
        FailShareList,
        FailContacts,
        FailAddPermission
    }

    private sealed class AttributionHandler : HttpMessageHandler
    {
        private readonly Mode _mode;

        public AttributionHandler(Mode mode) => _mode = mode;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri == null
                ? ""
                : request.RequestUri.IsAbsoluteUri
                    ? request.RequestUri.AbsolutePath.TrimStart('/')
                    : request.RequestUri.OriginalString.TrimStart('/');

            if (path.Contains("api/Share/List", StringComparison.OrdinalIgnoreCase)
                || path.Contains("api/Share/ListGrants", StringComparison.OrdinalIgnoreCase))
            {
                if (_mode == Mode.FailShareList)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                return Task.FromResult(Json(Array.Empty<object>()));
            }

            if (path.Contains("api/Entities/GetEntities", StringComparison.OrdinalIgnoreCase))
            {
                if (_mode == Mode.FailContacts)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                return Task.FromResult(Json(Array.Empty<object>()));
            }

            if (path.Contains($"api/Entities/GetEntity/{Target:D}", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Json(new { id = Target, name = "Sergey L.", code = (string?)null }));

            if (path.Contains("api/Entities/AddPermission", StringComparison.OrdinalIgnoreCase))
            {
                if (_mode == Mode.FailAddPermission)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(object body) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
    }
}
