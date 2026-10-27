using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Models.Share;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.Services;

public class CloudApiService
{
    /// <summary>Same page size the Cloud tab uses for GetEntities (M3 picker must not exceed this).</summary>
    public const int GetEntitiesPageSize = 200;

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
        return await GetEntitiesPagedAsync(new GetEntitiesContract
        {
            ParentIds = parentIds,
            IncludeValues = includeValues
        });
    }

    /// <summary>S6 Shared with me: caller's direct share roots (AT-S17).</summary>
    public async Task<List<GetEntitiesResult>> GetSharedWithMeAsync(bool includeValues = true)
    {
        return await GetEntitiesPagedAsync(new GetEntitiesContract
        {
            SharedWithMe = true,
            IncludeValues = includeValues
        });
    }

    /// <summary>Fetch entities by id (empty list when none are visible / gone).</summary>
    public async Task<List<GetEntitiesResult>> GetEntitiesByIdsAsync(Guid[] ids, bool includeValues = true)
    {
        if (ids.Length == 0) return new List<GetEntitiesResult>();
        return await GetEntitiesPagedAsync(new GetEntitiesContract
        {
            Ids = ids,
            IncludeValues = includeValues
        });
    }

    /// <summary>Immediate parents of <paramref name="entityId"/> (API <c>IdsToFindParents</c>).</summary>
    public async Task<List<GetEntitiesResult>> GetParentEntitiesAsync(Guid entityId, bool includeValues = true)
    {
        return await GetEntitiesPagedAsync(new GetEntitiesContract
        {
            IdsToFindParents = new[] { entityId },
            IncludeValues = includeValues
        });
    }

    private async Task<List<GetEntitiesResult>> GetEntitiesPagedAsync(GetEntitiesContract template)
    {
        var pageSize = GetEntitiesPageSize;
        var all = new List<GetEntitiesResult>();
        var skip = 0;
        while (true)
        {
            var response = await _http.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
            {
                Ids = template.Ids,
                ParentIds = template.ParentIds,
                IdsToFindParents = template.IdsToFindParents,
                IncludeValues = template.IncludeValues,
                SharedWithMe = template.SharedWithMe,
                ConversationUserIds = template.ConversationUserIds,
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

    /// <summary>M3 — list permission rows for shareability (sender write / recipient read).</summary>
    public async Task<List<EntityPermissionRow>> GetPermissionsAsync(Guid entityId)
    {
        var response = await _http.GetAsync($"api/Entities/GetPermissions?entityId={entityId:D}");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<List<EntityPermissionRow>>()
               ?? new List<EntityPermissionRow>();
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

    /// <param name="permissions">Optional grants written in the same create call (messenger media M4). Do not use AddPermission for a fresh entity.</param>
    /// <param name="mimeType">Optional mime string value (CloudFile); parent slot <see cref="CloudEntityCodes.MimeTypeId"/>.</param>
    public async Task<Guid> AddEntityAsync(
        string code,
        Guid[] parentIds,
        string? displayName = null,
        IReadOnlyList<AddPermissionContract>? permissions = null,
        string? mimeType = null)
    {
        List<AddValueContract>? values = null;
        if (displayName != null || mimeType != null)
        {
            values = new List<AddValueContract>();
            if (displayName != null)
            {
                values.Add(new AddValueContract
                {
                    Type = TypeOfValue.String,
                    Value = displayName,
                    ParentIds = new[] { CloudEntityCodes.NameTypeId }
                });
            }

            if (mimeType != null)
            {
                values.Add(new AddValueContract
                {
                    Type = TypeOfValue.String,
                    Value = mimeType,
                    ParentIds = new[] { CloudEntityCodes.MimeTypeId }
                });
            }
        }

        var contract = new AddEntityContract
        {
            Code = code,
            ParentIds = parentIds,
            Values = values,
            Permissions = permissions is { Count: > 0 } ? permissions.ToList() : null
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

    /// <summary>
    /// Original bytes via getFile with 403/404 discrimination (messenger tombstones).
    /// Does not throw for NotFound / Forbidden.
    /// </summary>
    public async Task<CloudFileBytesResult> TryGetFileBytesAsync(
        Guid entityId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(
            $"api/Files/getFile/{entityId:D}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        return await ReadBytesResultAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Derived preview via getImage?width=&amp;height= (messenger bubbles / lightbox).
    /// Does not throw for NotFound / Forbidden.
    /// </summary>
    public async Task<CloudFileBytesResult> TryGetImageBytesAsync(
        Guid entityId,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/Files/getImage/{entityId:D}?width={width}&height={height}";
        using var response = await _http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        return await ReadBytesResultAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<CloudFileBytesResult> ReadBytesResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
            return CloudFileBytesResult.NotFound();
        if (response.StatusCode == HttpStatusCode.Forbidden)
            return CloudFileBytesResult.NoAccess();
        if (!response.IsSuccessStatusCode)
        {
            var body = response.Content != null
                ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
                : null;
            return CloudFileBytesResult.Failed((int)response.StatusCode, body);
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return CloudFileBytesResult.Ok(bytes, contentType);
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

    /// <param name="progress">Optional byte progress for the multipart body (Cloud / messenger / sync).</param>
    public async Task AddFileAsync(
        Guid entityId,
        string fileName,
        Stream content,
        string mimeType,
        IProgress<CloudUploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        long? total = null;
        try
        {
            if (content.CanSeek)
                total = content.Length;
        }
        catch
        {
            total = null;
        }

        // Progress wrapper does not dispose <paramref name="content"/>; StreamContent may dispose the wrapper only.
        Stream uploadBody = progress != null
            ? new ProgressReadStream(content, progress, total)
            : content;
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(entityId.ToString()), "EntityId");
        var streamContent = new StreamContent(uploadBody);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        if (total is > 0)
            streamContent.Headers.ContentLength = total;
        form.Add(streamContent, "File", fileName);
        var response = await _http.PostAsync("api/Files/addFile", form, cancellationToken);
        await EnsureSuccessAsync(response);

        // Live API may bump entity version with mime-only values and drop NAME_TYPE from the
        // current version (→ UI/sync see file-{guid}). Re-assert display name so sync can match.
        if (!string.IsNullOrWhiteSpace(fileName)
            && !string.Equals(fileName, $"file-{entityId:N}", StringComparison.OrdinalIgnoreCase))
        {
            await AddValueAsync(entityId, fileName);
        }
    }

    /// <summary>
    /// Cycle-2 atomic messenger media send (client-contract §9). Server creates CloudFiles + Message
    /// and emits <c>message_sent</c>; mid-flight failure rolls back so the peer sees nothing.
    /// </summary>
    public async Task<MessengerSendWithAttachmentsResult> SendWithAttachmentsAsync(
        Guid partnerUserId,
        Guid? chatFolderId,
        string? bodyText,
        IReadOnlyList<Guid> existingAttachmentIds,
        IReadOnlyList<(string FileName, string MimeType, Stream Content, long? Length)> newFiles,
        IProgress<CloudUploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(partnerUserId.ToString("D")), "partnerUserId");
        if (chatFolderId is { } folder && folder != Guid.Empty)
            form.Add(new StringContent(folder.ToString("D")), "chatFolderId");
        if (!string.IsNullOrWhiteSpace(bodyText))
            form.Add(new StringContent(bodyText), "bodyText");
        form.Add(
            new StringContent(MessengerAtomicSend.FormatExistingAttachmentIds(existingAttachmentIds)),
            "existingAttachmentIds");

        long grandTotal = 0;
        var lengths = new long?[newFiles.Count];
        for (var i = 0; i < newFiles.Count; i++)
        {
            var len = newFiles[i].Length;
            if (len is null && newFiles[i].Content.CanSeek)
            {
                try { len = newFiles[i].Content.Length; }
                catch { /* leave null */ }
            }

            lengths[i] = len;
            if (len is > 0)
                grandTotal += len.Value;
        }

        long byteOffset = 0;
        var wrappers = new List<ProgressReadStream>();
        try
        {
            for (var i = 0; i < newFiles.Count; i++)
            {
                var file = newFiles[i];
                var fileLen = lengths[i];
                IProgress<CloudUploadProgress>? fileProgress = null;
                if (progress != null && grandTotal > 0)
                {
                    var offset = byteOffset;
                    fileProgress = new Progress<CloudUploadProgress>(p =>
                        progress.Report(new CloudUploadProgress(offset + p.BytesSent, grandTotal)));
                }

                Stream uploadBody = fileProgress != null
                    ? new ProgressReadStream(file.Content, fileProgress, fileLen)
                    : file.Content;
                if (uploadBody is ProgressReadStream wrapped)
                    wrappers.Add(wrapped);

                var streamContent = new StreamContent(uploadBody);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(
                    string.IsNullOrWhiteSpace(file.MimeType) ? "application/octet-stream" : file.MimeType);
                if (fileLen is > 0)
                    streamContent.Headers.ContentLength = fileLen;
                form.Add(streamContent, "files", file.FileName);

                if (fileLen is > 0)
                    byteOffset += fileLen.Value;
            }

            var response = await _http.PostAsync(MessengerAtomicSend.EndpointPath, form, cancellationToken)
                .ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return MessengerAtomicSend.ParseResponse(json);
        }
        finally
        {
            // Progress wrappers only — caller owns source file streams.
            foreach (var w in wrappers)
                w.Dispose();
        }
    }

    public async Task<CreateShareLinkResult> CreateShareLinkAsync(CreateShareLinkContract contract)
    {
        var response = await _http.PostAsJsonAsync("api/Share/Create", contract);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<CreateShareLinkResult>()
               ?? throw new InvalidOperationException("Empty CreateShareLink response.");
    }

    public async Task<List<ShareLinkListItemResult>> ListShareLinksAsync(Guid entityId)
    {
        var response = await _http.GetAsync($"api/Share/List?entityId={entityId:D}");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<List<ShareLinkListItemResult>>()
               ?? new List<ShareLinkListItemResult>();
    }

    public async Task UpdateShareLinkAsync(UpdateShareLinkContract contract)
    {
        var response = await _http.PostAsJsonAsync("api/Share/Update", contract);
        await EnsureSuccessAsync(response);
    }

    public async Task RevokeShareLinkAsync(Guid shareLinkId)
    {
        var response = await _http.PostAsJsonAsync("api/Share/Revoke", new RevokeShareLinkContract
        {
            ShareLinkId = shareLinkId
        });
        await EnsureSuccessAsync(response);
    }

    public async Task<List<ShareGrantResult>> ListShareGrantsAsync(Guid entityId)
    {
        var response = await _http.GetAsync($"api/Share/ListGrants?entityId={entityId:D}");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<List<ShareGrantResult>>()
               ?? new List<ShareGrantResult>();
    }

    /// <summary>UX §7b / AT-S46: remove caller's direct personal grant (Can view OK). Idempotent.</summary>
    public async Task<LeaveSharedWithMeResult> LeaveSharedWithMeAsync(Guid entityId)
    {
        var response = await _http.PostAsJsonAsync("api/Share/LeaveSharedWithMe", new LeaveSharedWithMeContract
        {
            EntityId = entityId
        });
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<LeaveSharedWithMeResult>()
               ?? new LeaveSharedWithMeResult { EntityId = entityId, Left = true };
    }

    /// <summary>AT-S53: outbound share roots the caller can write (people/guest/public summary).</summary>
    public async Task<List<SharedByMeRootResult>> ListSharedByMeAsync(int skip = 0, int? take = null)
    {
        var qs = take is null
            ? $"api/Share/ListSharedByMe?skip={skip}"
            : $"api/Share/ListSharedByMe?skip={skip}&take={take.Value}";
        var response = await _http.GetAsync(qs);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<List<SharedByMeRootResult>>()
               ?? new List<SharedByMeRootResult>();
    }

    public async Task AddPermissionAsync(Guid entityId, Guid permissionForId, bool canWrite)
    {
        var response = await _http.PostAsJsonAsync("api/Entities/AddPermission", new AddPermissionContract
        {
            Id = entityId,
            PermissionForId = permissionForId,
            CanWrite = canWrite
        });
        await EnsureSuccessAsync(response);
    }

    public async Task RemovePermissionAsync(Guid entityId, Guid permissionForId)
    {
        var response = await _http.PostAsJsonAsync("api/Entities/RemovePermission", new
        {
            EntityId = entityId,
            PermissionForId = permissionForId
        });
        await EnsureSuccessAsync(response);
    }

    /// <summary>GetEntity card for paste-ID resolve (share-sheet-ux §4). Null when not found.</summary>
    public async Task<GetEntityCardResult?> GetEntityCardAsync(Guid id, bool fromCache = true)
    {
        var response = await _http.GetAsync($"api/Entities/GetEntity/{id:D}?fromCache={fromCache.ToString().ToLowerInvariant()}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (response.StatusCode == HttpStatusCode.Forbidden)
            return null;
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<GetEntityCardResult>();
    }

    /// <summary>
    /// Share Add paste resolve via Auth GetUser / GetUserById (Friends parity; email + GUID).
    /// Returns null when empty / not found / display name would be GUID-shaped.
    /// </summary>
    public async Task<(Guid Id, string DisplayName)?> ResolveSharePasteUserAsync(
        string? raw,
        CancellationToken ct = default)
    {
        var paste = ShareAddPeople.ClassifyPasteLookup(raw);
        if (paste.Kind == ShareAddPeople.PasteLookupKind.Empty)
            return null;

        var lookup = paste.Kind == ShareAddPeople.PasteLookupKind.ById
            ? new ContactUserLookup.Lookup(ContactUserLookup.Kind.ById, paste.Value)
            : new ContactUserLookup.Lookup(ContactUserLookup.Kind.ByLogin, paste.Value);

        var user = await ContactUserLookup.ResolveUserAsync(_http, lookup, ct).ConfigureAwait(false);
        if (user is null)
            return null;

        var name = ShareAddPeople.PickResolvedDisplayName(user.Value.UserName, user.Value.Login);
        if (name is null)
            return null;

        return (user.Value.Id, name);
    }

    /// <summary>Messenger contact user ids under the caller's UserContacts node (no new search API).</summary>
    public async Task<List<(Guid UserId, string ProvisionalName)>> ListMessengerContactUsersAsync(Guid currentUserId)
    {
        // Same pick/stop-spawn as Settings Add + Messenger load (never FirstOrDefault empty bag).
        var bagId = await UserContactsBagClient.PickExistingBagIdAsync(_http, currentUserId);
        if (bagId is not { } userContactsId || userContactsId == Guid.Empty)
            return new List<(Guid, string)>();

        var contacts = await GetEntitiesAsync(new[] { userContactsId }, includeValues: true);
        var result = new List<(Guid, string)>();
        var seen = new HashSet<Guid>();
        foreach (var c in contacts)
        {
            var uid = Utilities.ShareAddPeople.ExtractContactUserId(c);
            if (uid is not { } id || id == Guid.Empty || !seen.Add(id))
                continue;
            var provisional = !string.IsNullOrWhiteSpace(c.Code) && !Utilities.ShareAddPeople.IsGuidText(c.Code)
                ? c.Code!.Trim()
                : id.ToString("D");
            result.Add((id, provisional));
        }
        return result;
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

    /// <summary>Mime string value from CloudFile (parent slot MimeTypeId), or null.</summary>
    public static string? GetMimeType(GetEntitiesResult entity)
    {
        if (entity.Values == null) return null;
        foreach (var v in entity.Values)
        {
            if (v.Parents == null || !v.Parents.Contains(CloudEntityCodes.MimeTypeId))
                continue;
            var s = ValueAsTrimmedString(v.Value);
            if (!string.IsNullOrEmpty(s))
                return s;
        }

        return null;
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
