namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>RU/EN labels for E6 “Add local folder to Cloud…” wizard / hub CTA / Notes nudge.</summary>
public static class MapLocalFolderStrings
{
    public static bool IsRussian(string? uiLanguage) => ShareAccountStrings.IsRussian(uiLanguage);

    public static string HubCta(string? lang) =>
        IsRussian(lang) ? "Добавить локальную папку в облако…" : "Add local folder to Cloud…";

    public static string HubCtaAutomationName(string? lang) =>
        IsRussian(lang) ? "Добавить локальную папку в облако" : "Add local folder to Cloud";

    public static string MapThisFolder(string? lang) =>
        IsRussian(lang)
            ? "Сопоставить эту папку облака с локальной…"
            : "Map this Cloud folder to a local folder…";

    public static string NotesNudgeText(string? lang) =>
        IsRussian(lang)
            ? "Ещё не в облаке — добавить эту папку в Cloud…"
            : "Not in Cloud yet — Add this folder to Cloud…";

    public static string WizardTitle(string? lang) =>
        IsRussian(lang) ? "Локальная папка → Cloud" : "Local folder → Cloud";

    public static string LocalFolderLabel(string? lang) =>
        IsRussian(lang) ? "Локальная папка" : "Local folder";

    public static string ChangeFolder(string? lang) =>
        IsRussian(lang) ? "Изменить…" : "Change…";

    public static string DestinationLabel(string? lang) =>
        IsRussian(lang) ? "Папка в Cloud" : "Cloud destination";

    public static string CreateNewOption(string? lang) =>
        IsRussian(lang) ? "Создать новую папку" : "Create new Cloud folder";

    public static string PickExistingOption(string? lang) =>
        IsRussian(lang) ? "Выбрать существующую" : "Pick existing unmapped folder";

    public static string FolderNameLabel(string? lang) =>
        IsRussian(lang) ? "Имя папки" : "Folder name";

    public static string ConfirmWarn(string? lang) =>
        IsRussian(lang)
            ? "Первая синхронизация сверит обе стороны. Конфликты решаются через «Загрузить всё» / «Скачать всё»."
            : "First sync reconciles both sides. Conflicts may need Upload all / Download all.";

    public static string Confirm(string? lang) =>
        IsRussian(lang) ? "Сопоставить и синхронизировать" : "Map and sync";

    public static string Cancel(string? lang) =>
        IsRussian(lang) ? "Отмена" : "Cancel";

    public static string PickLocalTitle(string? lang) =>
        IsRussian(lang) ? "Выберите локальную папку" : "Select local folder to add";

    public static string LocalMissing(string? lang) =>
        IsRussian(lang)
            ? "Папка не найдена или недоступна."
            : "Folder does not exist or is not accessible.";

    public static string NoUnmappedFolders(string? lang) =>
        IsRussian(lang)
            ? "Нет незамапленных папок здесь. Создайте новую."
            : "No unmapped folders here. Create a new one.";
}
