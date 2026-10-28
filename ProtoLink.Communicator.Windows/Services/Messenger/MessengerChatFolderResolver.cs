using System.Net.Http;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.Services.Messenger;

/// <summary>
/// Cache-first chat folder under CloudRoot/Chats/{contact} (client-contract §4).
/// Never grants the folder to the peer — only CloudFile entities get Permissions on create.
/// </summary>
public sealed class MessengerChatFolderResolver
{
    private readonly CloudApiService _api;
    private readonly ISettingsService _settings;
    private readonly object _gate = new();

    public MessengerChatFolderResolver(CloudApiService api, ISettingsService settings)
    {
        _api = api;
        _settings = settings;
    }

    public static string CacheKey(Guid ownerUserId, Guid partnerUserId) =>
        $"{ownerUserId:D}:{partnerUserId:D}";

    public Guid? TryGetCached(Guid ownerUserId, Guid partnerUserId)
    {
        var key = CacheKey(ownerUserId, partnerUserId);
        lock (_gate)
        {
            var settings = _settings.LoadSettings();
            if (settings.MessengerChatFolderIds != null
                && settings.MessengerChatFolderIds.TryGetValue(key, out var id)
                && id != Guid.Empty)
                return id;
        }

        return null;
    }

    public void SetCached(Guid ownerUserId, Guid partnerUserId, Guid folderId)
    {
        var key = CacheKey(ownerUserId, partnerUserId);
        lock (_gate)
        {
            var settings = _settings.LoadSettings();
            settings.MessengerChatFolderIds ??= new Dictionary<string, Guid>();
            settings.MessengerChatFolderIds[key] = folderId;
            _settings.SaveSettings(settings);
        }
    }

    public void ClearCached(Guid ownerUserId, Guid partnerUserId)
    {
        var key = CacheKey(ownerUserId, partnerUserId);
        lock (_gate)
        {
            var settings = _settings.LoadSettings();
            if (settings.MessengerChatFolderIds == null) return;
            if (settings.MessengerChatFolderIds.Remove(key))
                _settings.SaveSettings(settings);
        }
    }

    /// <summary>
    /// Returns a writable chat folder id. Uses cache when present; otherwise resolves/creates under Chats.
    /// </summary>
    public async Task<Guid> ResolveOrCreateAsync(
        Guid ownerUserId,
        Guid partnerUserId,
        string? partnerDisplayName,
        CancellationToken cancellationToken = default)
    {
        var cached = TryGetCached(ownerUserId, partnerUserId);
        if (cached is Guid id && id != Guid.Empty)
            return id;

        return await ReResolveAndCreateAsync(ownerUserId, partnerUserId, partnerDisplayName, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Single-call fallback after 403/404 on file create (stale cache).</summary>
    public async Task<Guid> ReResolveAndCreateAsync(
        Guid ownerUserId,
        Guid partnerUserId,
        string? partnerDisplayName,
        CancellationToken cancellationToken = default)
    {
        ClearCached(ownerUserId, partnerUserId);

        var cloudRootId = await EnsureCloudRootAsync(ownerUserId).ConfigureAwait(false);
        var chatsId = await EnsureNamedFolderAsync(cloudRootId, ChatFolderNaming.ChatsFolderName)
            .ConfigureAwait(false);

        var siblings = await _api.GetEntitiesAsync(new[] { chatsId }, includeValues: true)
            .ConfigureAwait(false);
        var siblingNames = siblings
            .Where(e => e.Code == CloudEntityCodes.CloudFolder)
            .Select(CloudApiService.GetDisplayName)
            .ToList();

        var folderName = ChatFolderNaming.ChooseFolderName(partnerDisplayName, partnerUserId, siblingNames);
        var existing = siblings.FirstOrDefault(e =>
            e.Code == CloudEntityCodes.CloudFolder
            && string.Equals(CloudApiService.GetDisplayName(e), folderName, StringComparison.OrdinalIgnoreCase));

        Guid folderId;
        if (existing != null)
            folderId = existing.Id;
        else
            folderId = await _api.AddEntityAsync(CloudEntityCodes.CloudFolder, new[] { chatsId }, folderName)
                .ConfigureAwait(false);

        SetCached(ownerUserId, partnerUserId, folderId);
        return folderId;
    }

    public static bool IsStaleFolderFailure(Exception ex)
    {
        if (ex is HttpRequestException http)
        {
            var msg = http.Message ?? string.Empty;
            return msg.Contains("HTTP 403", StringComparison.OrdinalIgnoreCase)
                   || msg.Contains("HTTP 404", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private async Task<Guid> EnsureCloudRootAsync(Guid ownerUserId)
    {
        var underUser = await _api.GetEntitiesAsync(new[] { ownerUserId }, includeValues: false)
            .ConfigureAwait(false);
        var candidates = underUser
            .Where(e => e.Code == CloudEntityCodes.CloudRoot)
            .Select(e => new CloudRootSelection.Candidate(e.Id, e.CreationTime))
            .ToList();
        var picked = CloudRootSelection.PickCanonical(candidates);
        if (picked is { } id && id != Guid.Empty)
            return id;
        return await _api.AddEntityAsync(CloudEntityCodes.CloudRoot, new[] { ownerUserId })
            .ConfigureAwait(false);
    }

    private async Task<Guid> EnsureNamedFolderAsync(Guid parentId, string name)
    {
        var children = await _api.GetEntitiesAsync(new[] { parentId }, includeValues: true)
            .ConfigureAwait(false);
        var hit = children.FirstOrDefault(e =>
            e.Code == CloudEntityCodes.CloudFolder
            && string.Equals(CloudApiService.GetDisplayName(e), name, StringComparison.OrdinalIgnoreCase));
        if (hit != null)
            return hit.Id;
        return await _api.AddEntityAsync(CloudEntityCodes.CloudFolder, new[] { parentId }, name)
            .ConfigureAwait(false);
    }
}
