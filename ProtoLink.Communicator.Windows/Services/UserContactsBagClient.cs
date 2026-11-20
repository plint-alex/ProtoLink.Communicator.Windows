using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// Shared UC bag list / pick / stop-spawn for Settings Add contact, Messenger, and Cloud contacts.
/// Aligns with ViewEditor ensureUserContacts (page-all + shouldCreate + recheck + enrich counts).
/// </summary>
public static class UserContactsBagClient
{
    /// <summary>Page all dual-parent UserContacts bags (newest-first API), parallel waves.</summary>
    public static Task<List<GetEntitiesResult>> ListAllBagsAsync(
        HttpClient http,
        Guid ownerUserId,
        CancellationToken ct = default) =>
        ListAllBagsAsync(http, ownerUserId, ct, seedFirstPage: null);

    /// <summary>
    /// Page all dual-parent UserContacts bags. When <paramref name="seedFirstPage"/> is set
    /// (StartupFast already fetched skip=0), that page is reused so escalate does not pay
    /// a duplicate first-page RTT before parallel remaining waves.
    /// </summary>
    public static async Task<List<GetEntitiesResult>> ListAllBagsAsync(
        HttpClient http,
        Guid ownerUserId,
        CancellationToken ct,
        IReadOnlyList<GetEntitiesResult>? seedFirstPage)
    {
        var bags = new List<GetEntitiesResult>();
        var skip = 0;
        var pagesDone = 0;

        if (seedFirstPage != null)
        {
            bags = UserContactsBagHelpers.MergeBagPages(bags, seedFirstPage);
            pagesDone++;
            if (!UserContactsBagHelpers.IsListPageFull(seedFirstPage))
                return bags;
            skip = UserContactsBagHelpers.ListPageSize;
        }
        else
        {
            // Sequential first page — empty/short list must not speculative-fetch later skips
            // (would merge a false "page 2" from race-guard snapshot stubs).
            var first = await FetchListPageAsync(http, ownerUserId, skip: 0, ct).ConfigureAwait(false);
            bags = UserContactsBagHelpers.MergeBagPages(bags, first);
            pagesDone++;
            if (!UserContactsBagHelpers.IsListPageFull(first))
                return bags;
            skip = UserContactsBagHelpers.ListPageSize;
        }

        while (pagesDone < UserContactsBagHelpers.ListMaxPages)
        {
            var remaining = UserContactsBagHelpers.ListMaxPages - pagesDone;
            var waveSize = Math.Min(UserContactsBagHelpers.ListPageParallelism, remaining);
            var waveSkips = new int[waveSize];
            for (var i = 0; i < waveSize; i++)
                waveSkips[i] = skip + i * UserContactsBagHelpers.ListPageSize;

            var wavePages = await Task.WhenAll(
                    waveSkips.Select(s => FetchListPageAsync(http, ownerUserId, s, ct)))
                .ConfigureAwait(false);

            var hitShort = false;
            for (var i = 0; i < wavePages.Length; i++)
            {
                bags = UserContactsBagHelpers.MergeBagPages(bags, wavePages[i]);
                pagesDone++;
                if (!UserContactsBagHelpers.IsListPageFull(wavePages[i]))
                {
                    hitShort = true;
                    break;
                }
            }

            if (hitShort)
                break;

            skip += waveSize * UserContactsBagHelpers.ListPageSize;
        }

        return bags;
    }

    /// <summary>
    /// When 2+ bags, count kids (take 500) so pick prefers the richest (Tina/person rows).
    /// Oldest-first. When <paramref name="earlyExitOnPartner"/>, stop after the first
    /// partner-bearing bag (Messenger Interactive / ContinueContacts budget).
    /// </summary>
    public static async Task EnrichPersonRowCountsAsync(
        HttpClient http,
        IList<UserContactsBagHelpers.BagSnapshot> bags,
        CancellationToken ct = default,
        bool earlyExitOnPartner = false)
    {
        if (bags.Count < 2)
            return;

        const int enrichParallelism = 8;
        var ordered = UserContactsBagHelpers.OrderBagsForEnrich(bags);
        using var gate = new SemaphoreSlim(enrichParallelism, enrichParallelism);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tasks = ordered.Select(async bag =>
        {
            try
            {
                await gate.WaitAsync(stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                if (stop.IsCancellationRequested)
                    return;

                var response = await http.PostAsJsonAsync(
                    "api/Entities/GetEntities",
                    new GetEntitiesContract
                    {
                        ParentIds = new[] { bag.Id },
                        Skip = 0,
                        Take = UserContactsBagHelpers.KidsTake,
                        IncludeValues = false
                    },
                    stop.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    bag.PersonRowCount = 0;
                    return;
                }

                var kids = await response.Content.ReadFromJsonAsync<List<GetEntitiesResult>>(cancellationToken: stop.Token)
                    .ConfigureAwait(false);
                bag.PersonRowCount = kids?.Count ?? 0;
                if (earlyExitOnPartner
                    && bag.PersonRowCount >= UserContactsBagHelpers.PartnerRowThreshold)
                {
                    try
                    {
                        stop.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // stop already torn down — fine
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Early-exit or outer cancel — leave count unset/0; pick still uses known counts.
            }
            catch
            {
                bag.PersonRowCount = 0;
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Pick richest/oldest bag; create only when no id-bearing dual-parent bag exists (recheck race guard).
    /// </summary>
    public static async Task<Guid?> EnsureBagIdAsync(
        HttpClient http,
        Guid ownerUserId,
        CancellationToken ct = default)
    {
        // Early-exit enrich: ~100+ thin spawn bags must not block Messenger ContinueContacts
        // (full kid scan exceeds Refresh generation). Oldest partner-bearing bag is enough.
        var picked = await TryPickEnrichedAsync(http, ownerUserId, ct, earlyExitOnPartner: true)
            .ConfigureAwait(false);
        if (picked.HasValue)
            return picked;

        // Race guard: empty first snapshot must not AddEntity when bags already exist.
        picked = await TryPickEnrichedAsync(http, ownerUserId, ct, earlyExitOnPartner: true)
            .ConfigureAwait(false);
        if (picked.HasValue)
            return picked;

        return await CreateBagAsync(http, ownerUserId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Startup-fast bag for Messenger contacts ASAP (#messenger-startup-priority).
    /// Short first page: enrich kid counts then pick (same <see cref="UserContactsBagHelpers.PickBag"/>).
    /// Full first page (newest-first truncation): escalate to page-all + enrich so partner bags
    /// off-page (Tina) are not hidden — do not pick thin newest bags without counts.
    /// </summary>
    public static async Task<Guid?> EnsureBagIdStartupFastAsync(
        HttpClient http,
        Guid ownerUserId,
        CancellationToken ct = default)
    {
        var first = await ListFirstPageBagsAsync(http, ownerUserId, ct).ConfigureAwait(false);
        if (UserContactsBagHelpers.ShouldCreateBag(first))
        {
            // Race guard: empty first page — recheck full list before create.
            var pickedEmpty = await TryPickEnrichedAsync(http, ownerUserId, ct, earlyExitOnPartner: true)
                .ConfigureAwait(false);
            if (pickedEmpty.HasValue)
                return pickedEmpty;

            return await CreateBagAsync(http, ownerUserId, ct).ConfigureAwait(false);
        }

        // Truncated newest-first page can hide older partner bags — page-all (reuse first) + early-exit enrich.
        if (!UserContactsBagHelpers.CanPickFromStartupFastFirstPage(first))
            return await TryPickEnrichedAsync(
                    http, ownerUserId, ct, earlyExitOnPartner: true, seedFirstPage: first)
                .ConfigureAwait(false);

        var snapshots = UserContactsBagHelpers.ToSnapshots(first);
        await EnrichPersonRowCountsAsync(http, snapshots, ct, earlyExitOnPartner: true)
            .ConfigureAwait(false);
        return UserContactsBagHelpers.PickBagId(snapshots);
    }

    /// <summary>First dual-parent UserContacts page only (newest-first API).</summary>
    public static async Task<List<GetEntitiesResult>> ListFirstPageBagsAsync(
        HttpClient http,
        Guid ownerUserId,
        CancellationToken ct = default) =>
        await FetchListPageAsync(http, ownerUserId, skip: 0, ct).ConfigureAwait(false);

    private static async Task<List<GetEntitiesResult>> FetchListPageAsync(
        HttpClient http,
        Guid ownerUserId,
        int skip,
        CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync(
            "api/Entities/GetEntities",
            new GetEntitiesContract
            {
                ParentIds = new[] { SystemEntities.Contacts, ownerUserId },
                Skip = skip,
                Take = UserContactsBagHelpers.ListPageSize,
                IncludeValues = false
            },
            ct).ConfigureAwait(false);
        var chunk = response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<List<GetEntitiesResult>>(cancellationToken: ct)
                .ConfigureAwait(false)
            : null;
        return chunk ?? new List<GetEntitiesResult>();
    }

    private static async Task<Guid?> CreateBagAsync(
        HttpClient http,
        Guid ownerUserId,
        CancellationToken ct)
    {
        var addResponse = await http.PostAsJsonAsync(
            "api/Entities/AddEntity",
            new AddEntityContract
            {
                Code = "UserContacts",
                ParentIds = new[] { SystemEntities.Contacts, ownerUserId }
            },
            ct).ConfigureAwait(false);
        if (!addResponse.IsSuccessStatusCode)
            return null;

        var addResult = await addResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)
            .ConfigureAwait(false);
        if (addResult.TryGetProperty("id", out var idEl)
            && idEl.TryGetGuid(out var created)
            && created != Guid.Empty)
            return created;
        return null;
    }

    /// <summary>Pick without creating (Cloud share contacts list).</summary>
    public static async Task<Guid?> PickExistingBagIdAsync(
        HttpClient http,
        Guid ownerUserId,
        CancellationToken ct = default) =>
        await TryPickEnrichedAsync(http, ownerUserId, ct, earlyExitOnPartner: true).ConfigureAwait(false);

    private static async Task<Guid?> TryPickEnrichedAsync(
        HttpClient http,
        Guid ownerUserId,
        CancellationToken ct,
        bool earlyExitOnPartner = true,
        IReadOnlyList<GetEntitiesResult>? seedFirstPage = null)
    {
        var entities = await ListAllBagsAsync(http, ownerUserId, ct, seedFirstPage).ConfigureAwait(false);
        if (UserContactsBagHelpers.ShouldCreateBag(entities))
            return null;

        var snapshots = UserContactsBagHelpers.ToSnapshots(entities);
        await EnrichPersonRowCountsAsync(http, snapshots, ct, earlyExitOnPartner).ConfigureAwait(false);
        return UserContactsBagHelpers.PickBagId(snapshots);
    }
}
