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
}
