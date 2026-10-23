using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels.Share;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Share Add people: auto-resolve pasted GUID without mandatory … click (UX §4).</summary>
[Trait("Category", "Offline")]
public class ShareSheetViewModelPasteResolveTests
{
    private static readonly Guid Me = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid Target = Guid.Parse("8f3a1111-2222-3333-4444-555555555555");

    [Fact]
    public async Task PasteValidGuid_AutoResolves_EnablesAdd_WithoutResolveClick()
    {
        var handler = new SharePasteHandler(Target, "Sergey L.");
        var vm = await OpenAddPeopleAsync(handler);

        Assert.False(vm.AddPeopleCommand.CanExecute(null));
        vm.PasteId = Target.ToString("D");

        await WaitForAsync(() => vm.HasPastePreview, TimeSpan.FromSeconds(2));

        Assert.Equal("Sergey L.", vm.PastePreviewName);
        Assert.False(vm.HasPastePreviewError);
        Assert.True(vm.AddPeopleCommand.CanExecute(null));
        Assert.Contains(handler.Paths, p => p.Contains($"api/Entities/GetEntity/{Target:D}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PasteUrlWithGuid_AutoResolves()
    {
        var handler = new SharePasteHandler(Target, "Sergey L.");
        var vm = await OpenAddPeopleAsync(handler);

        vm.PasteId = $"https://protolink.ru/{Target:D}?lang=ru-RU";
        await WaitForAsync(() => vm.HasPastePreview, TimeSpan.FromSeconds(2));

        Assert.Equal("Sergey L.", vm.PastePreviewName);
        Assert.True(vm.AddPeopleCommand.CanExecute(null));
    }

    [Fact]
    public async Task PasteEmail_DoesNotResolve_AddStaysDisabled()
    {
        var handler = new SharePasteHandler(Target, "Sergey L.");
        var vm = await OpenAddPeopleAsync(handler);

        vm.PasteId = "user@example.com";
        await Task.Delay(80);

        Assert.False(vm.HasPastePreview);
        Assert.False(vm.HasPastePreviewError);
        Assert.False(vm.AddPeopleCommand.CanExecute(null));
        Assert.DoesNotContain(handler.Paths, p => p.Contains("GetEntity/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PasteUnknownGuid_ShowsError_AddStaysDisabled()
    {
        var handler = new SharePasteHandler(Target, name: null);
        var vm = await OpenAddPeopleAsync(handler);

        vm.PasteId = Target.ToString("D");
        await WaitForAsync(() => vm.HasPastePreviewError, TimeSpan.FromSeconds(2));

        Assert.False(vm.HasPastePreview);
        Assert.False(vm.AddPeopleCommand.CanExecute(null));
        Assert.Contains("No ProtoLink user", vm.PastePreviewError ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ShareSheetViewModel> OpenAddPeopleAsync(SharePasteHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var api = new CloudApiService(http);
        var vm = new ShareSheetViewModel(
            api,
            () => "en-US",
            currentUserId: () => Me,
            pasteResolveDebounce: TimeSpan.Zero);

        await vm.OpenAsync(Guid.NewGuid(), "Demo", isFolder: true, childCount: 1, canEdit: true, isOnline: true);
        Assert.True(vm.BeginAddPeopleCommand.CanExecute(null));
        vm.BeginAddPeopleCommand.Execute(null);
        await WaitForAsync(() => vm.ShowAddPeople, TimeSpan.FromSeconds(2));
        // Let contact load (empty) finish so _busy/_paste path is idle.
        await Task.Delay(50);
        return vm;
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

    private sealed class SharePasteHandler : HttpMessageHandler
    {
        private readonly Guid _targetId;
        private readonly string? _name;

        public List<string> Paths { get; } = new();

        public SharePasteHandler(Guid targetId, string? name)
        {
            _targetId = targetId;
            _name = name;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri == null
                ? ""
                : request.RequestUri.IsAbsoluteUri
                    ? request.RequestUri.AbsolutePath.TrimStart('/')
                    : request.RequestUri.OriginalString.TrimStart('/');
            Paths.Add(path);

            if (path.Contains("api/Share/List", StringComparison.OrdinalIgnoreCase)
                || path.Contains("api/Share/ListGrants", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Json(Array.Empty<object>()));

            if (path.Contains("api/Entities/GetEntities", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Json(Array.Empty<object>()));

            if (path.Contains($"api/Entities/GetEntity/{_targetId:D}", StringComparison.OrdinalIgnoreCase))
            {
                if (_name == null)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                return Task.FromResult(Json(new { id = _targetId, name = _name, code = (string?)null }));
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
