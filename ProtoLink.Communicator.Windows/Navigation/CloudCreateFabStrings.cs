namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>RU/EN labels for the Cloud list create FAB (cycle1-next-polish UX-C-F).</summary>
public static class CloudCreateFabStrings
{
    public static bool IsRussian(string? uiLanguage) => ShareAccountStrings.IsRussian(uiLanguage);

    public static string FabName(string? lang) =>
        IsRussian(lang) ? "Создать" : "Create";

    public static string FabToolTip(string? lang) =>
        IsRussian(lang) ? "Создать папку или загрузить файл" : "New folder or upload file";

    public static string NewFolder(string? lang) =>
        IsRussian(lang) ? "Новая папка" : "New folder";

    public static string UploadFile(string? lang) =>
        IsRussian(lang) ? "Загрузить файл" : "Upload file";
}
