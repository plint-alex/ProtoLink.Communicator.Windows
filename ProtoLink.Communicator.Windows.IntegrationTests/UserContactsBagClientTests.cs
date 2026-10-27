using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Services;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Ensure/pick: page-all, never spawn when bags exist, enrich prefers rich bag.</summary>
[Trait("Category", "Offline")]
public class UserContactsBagClientTests
{
    private static readonly Guid Owner = Guid.Parse("a52b6095-7952-4535-90a1-ce8bdaf7f9c7");
    private static readonly Guid EmptyNewest = Guid.Parse("8f7fa2da-1111-4000-8000-000000000001");
    private static readonly Guid RichTina = Guid.Parse("0559e5ef-2222-4000-8000-000000000002");

    [Fact]
    public async Task Ensure_NeverAddEntity_WhenAnyBagExists_PicksRichByKidCount()
    {
        var handler = new UcHandler(
            listPages: new[]
            {
                new[]
                {
                    BagJson(EmptyNewest, "2026-10-27T00:00:00Z"),
                    BagJson(RichTina, "2026-01-01T00:00:00Z"),
                }
            },
            kidsByBag: new Dictionary<Guid, int>
            {
                [EmptyNewest] = 0,
                [RichTina] = 3
            });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var id = await UserContactsBagClient.EnsureBagIdAsync(http, Owner);

        Assert.Equal(RichTina, id);
        Assert.Equal(0, handler.AddEntityCount);
        Assert.True(handler.GetEntitiesCount >= 1);
    }

    [Fact]
    public async Task Ensure_CreatesOnly_WhenListEmpty_AfterRecheck()
    {
        var created = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var handler = new UcHandler(listPages: new[] { Array.Empty<object>(), Array.Empty<object>() }, createdId: created);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var id = await UserContactsBagClient.EnsureBagIdAsync(http, Owner);

        Assert.Equal(created, id);
        Assert.Equal(1, handler.AddEntityCount);
    }

    [Fact]
    public async Task Ensure_Recheck_StopsSpawn_WhenSecondListHasBag()
    {
        var handler = new UcHandler(
            listPages: new[]
            {
                Array.Empty<object>(),
                new[] { BagJson(RichTina, "2026-01-01T00:00:00Z") }
            },
            kidsByBag: new Dictionary<Guid, int> { [RichTina] = 1 });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var id = await UserContactsBagClient.EnsureBagIdAsync(http, Owner);

        Assert.Equal(RichTina, id);
        Assert.Equal(0, handler.AddEntityCount);
    }

    [Fact]
    public async Task ListAll_PagesUntilShort()
    {
        var page1 = Enumerable.Range(0, 50)
            .Select(i => BagJson(Guid.Parse($"10000000-0000-4000-8000-{i:D12}"), "2026-10-01T00:00:00Z"))
            .Cast<object>()
            .ToArray();
        var page2 = new[] { BagJson(RichTina, "2025-01-01T00:00:00Z") };
        var handler = new UcHandler(listPages: new[] { page1, page2 });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var bags = await UserContactsBagClient.ListAllBagsAsync(http, Owner);

        Assert.Equal(51, bags.Count);
        Assert.Contains(bags, b => b.Id == RichTina);
        Assert.Equal(2, handler.ListPageFetches);
    }

    private static object BagJson(Guid id, string creationTime) => new
    {
        id = id.ToString("D"),
        code = "UserContacts",
        creationTime,
        updateTime = creationTime,
        version = 1
    };

    private sealed class UcHandler : HttpMessageHandler
    {
        private readonly Queue<object[]> _listPages;
        private readonly Dictionary<Guid, int> _kidsByBag;
        private readonly Guid? _createdId;

        public int AddEntityCount { get; private set; }
        public int GetEntitiesCount { get; private set; }
        public int ListPageFetches { get; private set; }

        public UcHandler(
            IEnumerable<object[]> listPages,
            Dictionary<Guid, int>? kidsByBag = null,
            Guid? createdId = null)
        {
            _listPages = new Queue<object[]>(listPages);
            _kidsByBag = kidsByBag ?? new Dictionary<Guid, int>();
            _createdId = createdId;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("AddEntity", StringComparison.OrdinalIgnoreCase))
            {
                AddEntityCount++;
                var id = _createdId ?? Guid.NewGuid();
                return Json(new { id = id.ToString("D") });
            }

            if (path.Contains("GetEntities", StringComparison.OrdinalIgnoreCase))
            {
                GetEntitiesCount++;
                var body = request.Content == null
                    ? ""
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                var root = doc.RootElement;
                var parents = root.TryGetProperty("parentIds", out var p) || root.TryGetProperty("ParentIds", out p)
                    ? p.EnumerateArray().Select(x => Guid.Parse(x.GetString()!)).ToList()
                    : new List<Guid>();

                // Dual-parent list: CONTACTS + owner
                if (parents.Count == 2
                    && parents.Contains(SystemEntities.Contacts)
                    && parents.Contains(Owner))
                {
                    ListPageFetches++;
                    var page = _listPages.Count > 0 ? _listPages.Dequeue() : Array.Empty<object>();
                    return Json(page);
                }

                // Kids under a single bag id
                if (parents.Count == 1 && _kidsByBag.TryGetValue(parents[0], out var kidCount))
                {
                    var kids = Enumerable.Range(0, kidCount)
                        .Select(i => new
                        {
                            id = Guid.NewGuid().ToString("D"),
                            code = Guid.NewGuid().ToString("D"),
                            creationTime = "2026-01-01T00:00:00Z",
                            updateTime = "2026-01-01T00:00:00Z",
                            version = 1
                        })
                        .Cast<object>()
                        .ToArray();
                    return Json(kids);
                }

                return Json(Array.Empty<object>());
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
