using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
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
    /// <summary>Page all dual-parent UserContacts bags (newest-first API).</summary>
    public static async Task<List<GetEntitiesResult>> ListAllBagsAsync(
        HttpClient http,
        Guid ownerUserId,
        CancellationToken ct = default)
    {
        var bags = new List<GetEntitiesResult>();
        var skip = 0;
        for (var page = 0; page < UserContactsBagHelpers.ListMaxPages; page++)
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
            chunk ??= new List<GetEntitiesResult>();
            bags = UserContactsBagHelpers.MergeBagPages(bags, chunk);
            if (!UserContactsBagHelpers.IsListPageFull(chunk))
                break;
            skip += UserContactsBagHelpers.ListPageSize;
        }

        return bags;
    }

    /// <summary>
    /// When 2+ bags, count kids (take 500) so pick prefers the richest (Tina/person rows).
    /// </summary>
    public static async Task EnrichPersonRowCountsAsync(
        HttpClient http,
        IList<UserContactsBagHelpers.BagSnapshot> bags,
        CancellationToken ct = default)
    {
        if (bags.Count < 2)
            return;

        foreach (var bag in bags)
        {
            try
            {
                var response = await http.PostAsJsonAsync(
                    "api/Entities/GetEntities",
                    new GetEntitiesContract
                    {
                        ParentIds = new[] { bag.Id },
                        Skip = 0,
                        Take = UserContactsBagHelpers.KidsTake,
                        IncludeValues = false
                    },
                    ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    bag.PersonRowCount = 0;
                    continue;
                }

                var kids = await response.Content.ReadFromJsonAsync<List<GetEntitiesResult>>(cancellationToken: ct)
                    .ConfigureAwait(false);
                bag.PersonRowCount = kids?.Count ?? 0;
            }
            catch
            {
                bag.PersonRowCount = 0;
            }
        }
    }

    /// <summary>
    /// Pick richest/oldest bag; create only when no id-bearing dual-parent bag exists (recheck race guard).
    /// </summary>
    public static async Task<Guid?> EnsureBagIdAsync(
        HttpClient http,
        Guid ownerUserId,
        CancellationToken ct = default)
    {
        var picked = await TryPickEnrichedAsync(http, ownerUserId, ct).ConfigureAwait(false);
        if (picked.HasValue)
            return picked;

        // Race guard: empty first snapshot must not AddEntity when bags already exist.
        picked = await TryPickEnrichedAsync(http, ownerUserId, ct).ConfigureAwait(false);
        if (picked.HasValue)
            return picked;

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
        await TryPickEnrichedAsync(http, ownerUserId, ct).ConfigureAwait(false);

    private static async Task<Guid?> TryPickEnrichedAsync(
        HttpClient http,
        Guid ownerUserId,
        CancellationToken ct)
    {
        var entities = await ListAllBagsAsync(http, ownerUserId, ct).ConfigureAwait(false);
        if (UserContactsBagHelpers.ShouldCreateBag(entities))
            return null;

        var snapshots = UserContactsBagHelpers.ToSnapshots(entities);
        await EnrichPersonRowCountsAsync(http, snapshots, ct).ConfigureAwait(false);
        return UserContactsBagHelpers.PickBagId(snapshots);
    }
}
