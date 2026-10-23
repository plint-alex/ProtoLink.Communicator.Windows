namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>Account / share chrome strings shared with the web client (share-sheet-ux).</summary>
public static class ShareAccountStrings
{
    public static bool IsRussian(string? uiLanguage) =>
        !string.IsNullOrWhiteSpace(uiLanguage)
        && uiLanguage.Trim().StartsWith("ru", StringComparison.OrdinalIgnoreCase);

    public static string CopyMyIdLabel(string? uiLanguage) =>
        IsRussian(uiLanguage) ? "Скопировать мой ID" : "Copy my ID";

    public static string CopyMyIdHint(string? uiLanguage) =>
        IsRussian(uiLanguage)
            ? "Отправьте это тем, кто хочет дать вам доступ"
            : "Share this with people who want to give you access";

    public static string CopiedLabel(string? uiLanguage) =>
        IsRussian(uiLanguage) ? "Скопировано" : "Copied";

    /// <summary>Messenger: copy another contact’s user Guid (not own Copy my ID).</summary>
    public static string CopyContactIdLabel(string? uiLanguage) =>
        IsRussian(uiLanguage) ? "Скопировать ID контакта" : "Copy contact id";

    /// <summary>D-format Guid for Add contact / Share paste; null when unresolved.</summary>
    public static string? FormatUserIdForClipboard(Guid userId) =>
        userId == Guid.Empty ? null : userId.ToString("D");

    /// <summary>Normalize a contact user id string to D-format; null when empty/invalid.</summary>
    public static string? FormatUserIdForClipboard(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;
        return Guid.TryParse(userId.Trim(), out var id) ? FormatUserIdForClipboard(id) : null;
    }
}
