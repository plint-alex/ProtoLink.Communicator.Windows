namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Chat folder display names per messenger-media client-contract §4.
/// Preferred = contact display name; on collision append short contact user id.
/// </summary>
public static class ChatFolderNaming
{
    public const string ChatsFolderName = "Chats";

    public static string NormalizeDisplayName(string? contactDisplayName) =>
        string.IsNullOrWhiteSpace(contactDisplayName) ? "Chat" : contactDisplayName.Trim();

    public static string SuffixedName(string preferred, Guid contactUserId) =>
        $"{preferred} ({ShortId(contactUserId)})";

    public static string ShortId(Guid contactUserId) =>
        contactUserId.ToString("N")[..8];

    /// <summary>
    /// Name to match or create under <c>CloudRoot/Chats</c>.
    /// If the preferred name is free, use it; if taken (or our suffixed name already exists), use the suffixed form.
    /// </summary>
    public static string ChooseFolderName(
        string? contactDisplayName,
        Guid contactUserId,
        IEnumerable<string> existingSiblingNames)
    {
        var preferred = NormalizeDisplayName(contactDisplayName);
        var suffixed = SuffixedName(preferred, contactUserId);
        var set = new HashSet<string>(
            existingSiblingNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (set.Contains(suffixed))
            return suffixed;
        if (set.Contains(preferred))
            return suffixed;
        return preferred;
    }

    /// <summary>
    /// Two contacts with the same display name must get two distinct folder names when both are considered.
    /// </summary>
    public static (string First, string Second) DistinctNamesForCollision(
        string sharedDisplayName,
        Guid firstContactId,
        Guid secondContactId)
    {
        var names = new List<string>();
        var first = ChooseFolderName(sharedDisplayName, firstContactId, names);
        names.Add(first);
        var second = ChooseFolderName(sharedDisplayName, secondContactId, names);
        return (first, second);
    }
}
