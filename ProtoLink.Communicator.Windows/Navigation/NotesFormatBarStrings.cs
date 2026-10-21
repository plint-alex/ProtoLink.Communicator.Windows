namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>RU/EN labels for the Notes floating format bar (cycle1-next-polish UX-N-F4).</summary>
public static class NotesFormatBarStrings
{
    public static bool IsRussian(string? uiLanguage) => ShareAccountStrings.IsRussian(uiLanguage);

    public static string Bold(string? lang) =>
        IsRussian(lang) ? "Жирный (Ctrl+B)" : "Bold (Ctrl+B)";

    public static string Italic(string? lang) =>
        IsRussian(lang) ? "Курсив (Ctrl+I)" : "Italic (Ctrl+I)";

    public static string Underline(string? lang) =>
        IsRussian(lang) ? "Подчёркнутый (Ctrl+U)" : "Underline (Ctrl+U)";

    public static string Strikethrough(string? lang) =>
        IsRussian(lang) ? "Зачёркнутый" : "Strikethrough";

    public static string TextStyle(string? lang) =>
        IsRussian(lang) ? "Стиль текста" : "Text style";

    public static string BulletList(string? lang) =>
        IsRussian(lang) ? "Маркированный список" : "Bullet list";

    public static string NumberedList(string? lang) =>
        IsRussian(lang) ? "Нумерованный список" : "Numbered list";

    public static string CheckboxList(string? lang) =>
        IsRussian(lang) ? "Список с флажками" : "Checkbox list";

    public static string InsertLink(string? lang) =>
        IsRussian(lang) ? "Вставить ссылку" : "Insert link";

    public static string CodeBlock(string? lang) =>
        IsRussian(lang) ? "Блок кода" : "Code block";

    public static string InsertTable(string? lang) =>
        IsRussian(lang) ? "Вставить таблицу" : "Insert table";

    public static string Normal(string? lang) =>
        IsRussian(lang) ? "Обычный" : "Normal";

    public static string Heading1(string? lang) =>
        IsRussian(lang) ? "Заголовок 1" : "Heading 1";

    public static string Heading2(string? lang) =>
        IsRussian(lang) ? "Заголовок 2" : "Heading 2";

    public static string Heading3(string? lang) =>
        IsRussian(lang) ? "Заголовок 3" : "Heading 3";
}
