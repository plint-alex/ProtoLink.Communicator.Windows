using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Services;

public class CloudApiService
{
    private readonly HttpClient _http;

    public CloudApiService(HttpClient http)
    {
        _http = http;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = response.Content != null ? await response.Content.ReadAsStringAsync() : null;
        if (string.IsNullOrWhiteSpace(body)) body = "(no response body from server)";

        if (response.StatusCode == HttpStatusCode.Unauthorized
            || (response.StatusCode == HttpStatusCode.Forbidden
                && body.Contains("User ID not found", StringComparison.OrdinalIgnoreCase)))
        {
            throw new CloudAuthRequiredException();
        }

        var requestUri = response.RequestMessage?.RequestUri?.ToString() ?? "(unknown)";
        throw new HttpRequestException(
            $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Request: {requestUri}{Environment.NewLine}Response: {body}");
    }

    public async Task<List<GetEntitiesResult>> GetEntitiesAsync(Guid[]? parentIds, bool includeValues = true)
    {
        const int pageSize = 200;
        var all = new List<GetEntitiesResult>();
        var skip = 0;
        while (true)
        {
            var response = await _http.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
            {
                ParentIds = parentIds,
                IncludeValues = includeValues,
                Skip = skip,
                Take = pageSize
            });
            await EnsureSuccessAsync(response);
            var list = await response.Content.ReadFromJsonAsync<List<GetEntitiesResult>>()
                       ?? new List<GetEntitiesResult>();
            all.AddRange(list);
            if (list.Count < pageSize) break;
            skip += pageSize;
        }
        return all;
    }

    public async Task AddParentAsync(Guid entityId, Guid parentId)
    {
        var response = await _http.PostAsJsonAsync("api/Entities/AddParent", new
        {
            Id = entityId,
            ParentId = parentId
        });
        await EnsureSuccessAsync(response);
    }

    public async Task RemoveParentAsync(Guid entityId, Guid parentId)
    {
        var response = await _http.PostAsJsonAsync("api/Entities/RemoveParent", new
        {
            Id = entityId,
            ParentId = parentId
        });
        await EnsureSuccessAsync(response);
    }

    public async Task<Guid> AddEntityAsync(string code, Guid[] parentIds, string? displayName = null)
    {
        var contract = new AddEntityContract
        {
            Code = code,
            ParentIds = parentIds,
            Values = displayName != null ? new List<AddValueContract>
            {
                new() { Type = TypeOfValue.String, Value = displayName, ParentIds = new[] { CloudEntityCodes.NameTypeId } }
            } : null
        };
        var response = await _http.PostAsJsonAsync("api/Entities/AddEntity", contract);
        await EnsureSuccessAsync(response);
        var doc = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var id = doc.GetProperty("id").GetGuid();
        if (id == Guid.Empty)
            throw new InvalidOperationException(
                "AddEntity returned an empty id. Sign in again or check that the API accepted the request.");
        return id;
    }

    public async Task AddValueAsync(Guid entityId, string value)
    {
        var contract = new AddValueContract
        {
            Type = TypeOfValue.String,
            Value = value,
            ParentIds = new[] { CloudEntityCodes.NameTypeId }
        };
        var response = await _http.PostAsJsonAsync($"api/Entities/AddValue/{entityId}", contract);
        await EnsureSuccessAsync(response);
    }

    public async Task DeleteEntityAsync(Guid id)
    {
        var response = await _http.PostAsJsonAsync("api/Entities/DeleteEntity", new DeleteEntityContract { Id = id });
        await EnsureSuccessAsync(response);
    }

    /// <summary>Stream file content by entity id (one entity = one file).</summary>
    public async Task<Stream> GetFileStreamAsync(Guid entityId)
    {
        var response = await _http.GetAsync($"api/Files/getFile/{entityId}");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadAsStreamAsync();
    }

    /// <summary>404 Not Found returns null; any other failure throws.</summary>
    public async Task<Stream?> GetFileStreamOrNotFoundAsync(Guid entityId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/Files/getFile/{entityId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response);
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    /// <summary>
    /// Content-Length from response headers only (no body read).
    /// OpenResty often serves getFile as chunked with no Content-Length — return null then.
    /// Never download the body here: enrich-all-files was stalling mapped sync forever.
    /// Same-size edits use ContentHash / unseeded remote hash probe in SyncEngine instead.
    /// </summary>
    public virtual async Task<long?> GetFileContentLengthOrNullAsync(Guid entityId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(
            $"api/Files/getFile/{entityId}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response);
        // Treat 0 as unknown — some servers/proxies report Content-Length: 0 incorrectly.
        var len = response.Content.Headers.ContentLength;
        if (len is null or <= 0
            && response.Content.Headers.TryGetValues("Content-Length", out var raw)
            && long.TryParse(raw.FirstOrDefault(), out var parsed)
            && parsed > 0)
            len = parsed;
        return len is > 0 ? len : null;
    }

    public async Task AddFileAsync(Guid entityId, string fileName, Stream content, string mimeType)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(entityId.ToString()), "EntityId");
        var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        form.Add(streamContent, "File", fileName);
        var response = await _http.PostAsync("api/Files/addFile", form);
        await EnsureSuccessAsync(response);

        // Live API may bump entity version with mime-only values and drop NAME_TYPE from the
        // current version (→ UI/sync see file-{guid}). Re-assert display name so sync can match.
        if (!string.IsNullOrWhiteSpace(fileName)
            && !string.Equals(fileName, $"file-{entityId:N}", StringComparison.OrdinalIgnoreCase))
        {
            await AddValueAsync(entityId, fileName);
        }
    }

    /// <summary>Push a SignalR command so other devices refresh (messenger/cloud/notes).</summary>
    public async Task SendRealtimeCommandAsync(string commandType, string targetUserId, object? parameters = null)
    {
        var response = await _http.PostAsJsonAsync("api/commands/send", new
        {
            commandType,
            targetUserId,
            parameters
        });
        // Non-fatal for callers — ignore failures.
        _ = response;
    }

    /// <summary>
    /// Human-facing name for UI and local sync paths. Never returns the entity type code (e.g. CloudFile) as a filename.
    /// </summary>
    public static string GetDisplayName(GetEntitiesResult entity)
    {
        string? fromNameType = null;
        string? otherNonMimeString = null;
        if (entity.Values != null)
        {
            foreach (var v in entity.Values)
            {
                var s = ValueAsTrimmedString(v.Value);
                if (string.IsNullOrEmpty(s)) continue;
                if (v.Parents != null && v.Parents.Contains(CloudEntityCodes.NameTypeId))
                {
                    fromNameType = s;
                    break;
                }

                if (otherNonMimeString == null && (v.Parents == null || !v.Parents.Contains(CloudEntityCodes.MimeTypeId)))
                    otherNonMimeString = s;
            }
        }

        if (!string.IsNullOrEmpty(fromNameType))
            return fromNameType;
        if (!string.IsNullOrEmpty(otherNonMimeString))
            return otherNonMimeString;

        if (entity.Code == CloudEntityCodes.CloudFile)
            return $"file-{entity.Id:N}";
        if (entity.Code == CloudEntityCodes.CloudFolder)
            return $"folder-{entity.Id:N}";
        return entity.Code ?? entity.Id.ToString();
    }

    /// <summary>
    /// True when <paramref name="displayName"/> is the fallback for a CloudFile with no stored name (<c>file-{id:N}</c>).
    /// Sync must not use that label as a real on-disk filename or it will miss matches and create duplicate cloud files.
    /// </summary>
    public static bool IsSyntheticCloudFileDisplayName(GetEntitiesResult entity, string displayName) =>
        entity.Code == CloudEntityCodes.CloudFile
        && string.Equals(displayName, $"file-{entity.Id:N}", StringComparison.OrdinalIgnoreCase);

    private static string? ValueAsTrimmedString(object? value)
    {
        if (value == null) return null;
        if (value is string str)
            return string.IsNullOrWhiteSpace(str) ? null : str.Trim();
        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.String)
            {
                var s = je.GetString();
                return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            }

            if (je.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                return je.ToString();
            return null;
        }

        var t = value.ToString();
        return string.IsNullOrWhiteSpace(t) ? null : t.Trim();
    }
}
