using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Utilities;
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
        // Race-guard snapshots: each ListAll sees empty at skip=0 (not multi-page).
        var handler = new UcHandler(
            listPages: Array.Empty<object[]>(),
            createdId: created,
            listSnapshots: new[] { Array.Empty<object>(), Array.Empty<object>() });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var id = await UserContactsBagClient.EnsureBagIdAsync(http, Owner);

        Assert.Equal(created, id);
        Assert.Equal(1, handler.AddEntityCount);
    }

    [Fact]
    public async Task Ensure_Recheck_StopsSpawn_WhenSecondListHasBag()
    {
        // First Ensure ListAll empty; recheck ListAll returns Tina (successive skip=0 snapshots).
        var handler = new UcHandler(
            listPages: Array.Empty<object[]>(),
            kidsByBag: new Dictionary<Guid, int> { [RichTina] = 1 },
            listSnapshots: new[]
            {
                Array.Empty<object>(),
                new[] { BagJson(RichTina, "2026-01-01T00:00:00Z") }
            });
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
        // Sequential first page + parallel wave (may over-fetch empty tail).
        Assert.True(handler.ListPageFetches >= 2, $"ListPageFetches={handler.ListPageFetches}");
        Assert.True(
            handler.ListPageFetches <= 1 + UserContactsBagHelpers.ListPageParallelism,
            $"ListPageFetches={handler.ListPageFetches}");
    }

    [Fact]
    public async Task StartupFast_ShortPage_EnrichesAndPicksPartnerBag()
    {
        // Short first page: enrich kid counts, prefer partner-bearing Tina over empty newest.
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

        var id = await UserContactsBagClient.EnsureBagIdStartupFastAsync(http, Owner);

        Assert.Equal(RichTina, id);
        Assert.Equal(0, handler.AddEntityCount);
        Assert.Equal(1, handler.ListPageFetches);
        // Dual-parent list + early-exit kid enrich (Tina oldest/partner) — may skip empty sibling.
        Assert.True(handler.GetEntitiesCount >= 2 && handler.GetEntitiesCount <= 3);
        Assert.True(handler.KidFetches >= 1 && handler.KidFetches <= 2);
    }

    [Fact]
    public async Task StartupFast_TruncatedFirstPage_EscalatesFullEnrich_PicksOffPageTina()
    {
        // Newest-first page full of thin bags; Tina only on page 2 — must escalate (not pick thin).
        var allBags = Enumerable.Range(0, 50)
            .Select(i => BagJson(
                Guid.Parse($"10000000-0000-4000-8000-{i:D12}"),
                "2026-10-27T00:00:00Z"))
            .Concat(new[] { BagJson(RichTina, "2025-01-01T00:00:00Z") })
            .Cast<object>()
            .ToArray();
        var kids = Enumerable.Range(0, 50)
            .ToDictionary(i => Guid.Parse($"10000000-0000-4000-8000-{i:D12}"), _ => 1);
        kids[RichTina] = 3;

        var handler = new UcHandler(listPages: Array.Empty<object[]>(), kidsByBag: kids, pagedBags: allBags);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var id = await UserContactsBagClient.EnsureBagIdStartupFastAsync(http, Owner);

        Assert.Equal(RichTina, id);
        Assert.Equal(0, handler.AddEntityCount);
        Assert.True(handler.ListPageFetches >= 2);
        Assert.True(handler.GetEntitiesCount > 2);
    }

    [Fact]
    public async Task StartupFast_Escalate_EarlyExitEnrich_DoesNotKidFetchAllHundredBags()
    {
        // ~100 thin newest + oldest Tina: early-exit must stop kid fetches well under 100.
        var thin = Enumerable.Range(0, 99)
            .Select(i => BagJson(
                Guid.Parse($"10000000-0000-4000-8000-{i:D12}"),
                $"2026-10-{(i % 27) + 1:D2}T00:00:00Z"))
            .Cast<object>()
            .ToArray();
        var allBags = thin.Concat(new[] { BagJson(RichTina, "2025-01-01T00:00:00Z") }).ToArray();
        var kids = Enumerable.Range(0, 99)
            .ToDictionary(i => Guid.Parse($"10000000-0000-4000-8000-{i:D12}"), _ => 1);
        kids[RichTina] = 3;

        var handler = new UcHandler(listPages: Array.Empty<object[]>(), kidsByBag: kids, pagedBags: allBags);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var id = await UserContactsBagClient.EnsureBagIdStartupFastAsync(http, Owner);

        Assert.Equal(RichTina, id);
        // Dual-parent pages (~2) + kid enrich: early-exit after Tina (oldest) — not 99+ kid calls.
        Assert.True(handler.KidFetches < 20, $"KidFetches={handler.KidFetches} should early-exit");
        Assert.True(handler.KidFetches >= 1);
    }

    [Fact]
    public async Task Ensure_EarlyExitEnrich_DoesNotKidFetchAllHundredBags()
    {
        // ContinueContactsAfterPaint uses EnsureBagIdAsync — must early-exit like StartupFast
        // or Refresh generation bump leaves SELF_ONLY forever.
        var thin = Enumerable.Range(0, 99)
            .Select(i => BagJson(
                Guid.Parse($"10000000-0000-4000-8000-{i:D12}"),
                $"2026-10-{(i % 27) + 1:D2}T00:00:00Z"))
            .Cast<object>()
            .ToArray();
        var allBags = thin.Concat(new[] { BagJson(RichTina, "2025-01-01T00:00:00Z") }).ToArray();
        var kids = Enumerable.Range(0, 99)
            .ToDictionary(i => Guid.Parse($"10000000-0000-4000-8000-{i:D12}"), _ => 1);
        kids[RichTina] = 3;

        var handler = new UcHandler(listPages: Array.Empty<object[]>(), kidsByBag: kids, pagedBags: allBags);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var id = await UserContactsBagClient.EnsureBagIdAsync(http, Owner);

        Assert.Equal(RichTina, id);
        Assert.True(handler.KidFetches < 20, $"KidFetches={handler.KidFetches} should early-exit");
    }

    [Fact]
    public async Task StartupFast_Truncated_ReusesFirstPage_DoesNotRefetchSkip0()
    {
        var allBags = Enumerable.Range(0, 50)
            .Select(i => BagJson(
                Guid.Parse($"10000000-0000-4000-8000-{i:D12}"),
                "2026-10-27T00:00:00Z"))
            .Concat(new[] { BagJson(RichTina, "2025-01-01T00:00:00Z") })
            .Cast<object>()
            .ToArray();
        var kids = Enumerable.Range(0, 50)
            .ToDictionary(i => Guid.Parse($"10000000-0000-4000-8000-{i:D12}"), _ => 1);
        kids[RichTina] = 3;

        var handler = new UcHandler(listPages: Array.Empty<object[]>(), kidsByBag: kids, pagedBags: allBags);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var id = await UserContactsBagClient.EnsureBagIdStartupFastAsync(http, Owner);

        Assert.Equal(RichTina, id);
        // First page once + remaining wave — skip=0 must not be fetched twice.
        Assert.Equal(1, handler.Skip0ListFetches);
        Assert.True(handler.ListPageFetches >= 2);
    }

    [Fact]
    public async Task StartupFast_CreatesWhenFirstPageEmpty_AfterRecheck()
    {
        var created = Guid.Parse("cccccccc-dddd-4eee-8fff-000000000001");
        var handler = new UcHandler(
            listPages: Array.Empty<object[]>(),
            createdId: created,
            listSnapshots: new[] { Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>() });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var id = await UserContactsBagClient.EnsureBagIdStartupFastAsync(http, Owner);

        Assert.Equal(created, id);
        Assert.Equal(1, handler.AddEntityCount);
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
        private readonly object[][] _listPages;
        private readonly Dictionary<Guid, int> _kidsByBag;
        private readonly Guid? _createdId;
        private readonly object[]? _pagedBags;
        private readonly Queue<object[]>? _listSnapshots;
        private readonly object _snapshotGate = new();

        public int AddEntityCount { get; private set; }
        public int GetEntitiesCount { get; private set; }
        public int ListPageFetches { get; private set; }
        public int Skip0ListFetches { get; private set; }
        public int KidFetches { get; private set; }

        public UcHandler(
            IEnumerable<object[]> listPages,
            Dictionary<Guid, int>? kidsByBag = null,
            Guid? createdId = null,
            object[]? pagedBags = null,
            IEnumerable<object[]>? listSnapshots = null)
        {
            // Indexed by skip/take — parallel list waves must not race a Queue.Dequeue.
            _listPages = listPages.ToArray();
            _kidsByBag = kidsByBag ?? new Dictionary<Guid, int>();
            _createdId = createdId;
            _pagedBags = pagedBags;
            _listSnapshots = listSnapshots != null ? new Queue<object[]>(listSnapshots) : null;
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
                var skip = root.TryGetProperty("skip", out var sk) || root.TryGetProperty("Skip", out sk)
                    ? sk.GetInt32()
                    : 0;
                var take = root.TryGetProperty("take", out var tk) || root.TryGetProperty("Take", out tk)
                    ? tk.GetInt32()
                    : 50;

                // Dual-parent list: CONTACTS + owner
                if (parents.Count == 2
                    && parents.Contains(SystemEntities.Contacts)
                    && parents.Contains(Owner))
                {
                    ListPageFetches++;
                    if (skip == 0)
                        Skip0ListFetches++;
                    if (_pagedBags != null)
                    {
                        var slice = _pagedBags.Skip(skip).Take(take).ToArray();
                        return Json(slice);
                    }

                    // Successive full-list snapshots (race-guard recheck) — only at skip=0.
                    if (_listSnapshots != null)
                    {
                        if (skip != 0)
                            return Json(Array.Empty<object>());
                        lock (_snapshotGate)
                        {
                            var snap = _listSnapshots.Count > 0
                                ? _listSnapshots.Dequeue()
                                : Array.Empty<object>();
                            return Json(snap);
                        }
                    }

                    var pageIndex = take > 0 ? skip / take : 0;
                    var page = pageIndex >= 0 && pageIndex < _listPages.Length
                        ? _listPages[pageIndex]
                        : Array.Empty<object>();
                    return Json(page);
                }

                // Kids under a single bag id
                if (parents.Count == 1 && _kidsByBag.TryGetValue(parents[0], out var kidCount))
                {
                    KidFetches++;
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
