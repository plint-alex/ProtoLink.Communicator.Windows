namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>E12 shared Cloud delete dialog copy (RU/EN).</summary>
public static class CloudSharedDeleteStrings
{
    public static string Title(string? lang) =>
        ShareSheetStrings.IsRussian(lang) ? "Удалить" : "Delete";

    public static string ForMeOnly(string? lang) =>
        ShareSheetStrings.IsRussian(lang) ? "Только для меня" : "For me only";

    public static string ForAll(string? lang) =>
        ShareSheetStrings.IsRussian(lang) ? "Для всех" : "For all users";

    public static string Cancel(string? lang) =>
        ShareSheetStrings.IsRussian(lang) ? "Отмена" : "Cancel";

    public static string Body(string? lang, string name, bool isFolder)
    {
        var safe = string.IsNullOrWhiteSpace(name) ? "—" : name.Trim();
        if (ShareSheetStrings.IsRussian(lang))
        {
            return isFolder
                ? $"«{safe}» доступна нескольким людям. Удалить только у себя или для всех?"
                : $"«{safe}» доступен нескольким людям. Удалить только у себя или для всех?";
        }

        return isFolder
            ? $"\"{safe}\" is shared. Remove it for you only, or delete it for everyone?"
            : $"\"{safe}\" is shared. Remove it for you only, or delete it for everyone?";
    }

    public static string DetachedStatus(string? lang, string name)
    {
        var safe = string.IsNullOrWhiteSpace(name) ? "—" : name.Trim();
        return ShareSheetStrings.IsRussian(lang)
            ? $"Убрано из Моего облака: «{safe}»"
            : $"Removed from My Cloud: \"{safe}\"";
    }

    /// <summary>AT-SFD-4: Api returned detached=false — do not paint success / remove the row.</summary>
    public static string DetachFailed(string? lang, string name)
    {
        var safe = string.IsNullOrWhiteSpace(name) ? "—" : name.Trim();
        return ShareSheetStrings.IsRussian(lang)
            ? $"Не удалось убрать из Моего облака: «{safe}»"
            : $"Could not remove from My Cloud: \"{safe}\"";
    }
}
