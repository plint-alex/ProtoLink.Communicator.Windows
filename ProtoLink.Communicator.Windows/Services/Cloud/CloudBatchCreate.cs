using System.Text.Json;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// Offline-testable helpers for Cloud parent+children batch create via AddEntities.
/// Single-entity creates stay on AddEntity (<see cref="ShouldUseBatch"/>).
/// </summary>
public static class CloudBatchCreate
{
    public sealed record PlannedItem(
        Guid TempId,
        string Code,
        Guid[] ParentIds,
        string? DisplayName);

    /// <summary>Batch when planting ≥2 new entities (parent+children / tree). One-offs stay on AddEntity.</summary>
    public static bool ShouldUseBatch(int createCount) => createCount >= 2;

    public static List<AddEntitiesItemContract> ToContracts(IReadOnlyList<PlannedItem> items)
    {
        var list = new List<AddEntitiesItemContract>(items.Count);
        foreach (var item in items)
            list.Add(ToContract(item));
        return list;
    }

    public static AddEntitiesItemContract ToContract(PlannedItem item)
    {
        List<AddValueContract>? values = null;
        if (item.DisplayName != null)
        {
            values =
            [
                new AddValueContract
                {
                    Type = TypeOfValue.String,
                    Value = item.DisplayName,
                    ParentIds = [CloudEntityCodes.NameTypeId]
                }
            ];
        }

        return new AddEntitiesItemContract
        {
            TempId = item.TempId,
            Code = item.Code,
            ParentIds = item.ParentIds,
            Values = values
        };
    }

    /// <summary>Parse <c>{ "ids": [ ... ] }</c> in request order.</summary>
    public static IReadOnlyList<Guid> ParseIdsResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseIdsResponse(doc.RootElement);
    }

    public static IReadOnlyList<Guid> ParseIdsResponse(JsonElement root)
    {
        if (!root.TryGetProperty("ids", out var idsEl) || idsEl.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("AddEntities response missing ids array.");

        var ids = new List<Guid>(idsEl.GetArrayLength());
        foreach (var el in idsEl.EnumerateArray())
        {
            var id = el.GetGuid();
            if (id == Guid.Empty)
                throw new InvalidOperationException("AddEntities returned an empty id.");
            ids.Add(id);
        }

        return ids;
    }

    /// <summary>
    /// Parents-before-children honesty: every ParentId that is a TempId of this batch must
    /// refer to an earlier item (not a later one).
    /// </summary>
    public static bool ParentsBeforeChildren(IReadOnlyList<PlannedItem> items)
    {
        var pending = new HashSet<Guid>();
        foreach (var item in items)
            pending.Add(item.TempId);

        var unresolved = new HashSet<Guid>(pending);
        foreach (var item in items)
        {
            foreach (var pid in item.ParentIds)
            {
                if (unresolved.Contains(pid))
                    return false;
            }

            unresolved.Remove(item.TempId);
        }

        return true;
    }
}
