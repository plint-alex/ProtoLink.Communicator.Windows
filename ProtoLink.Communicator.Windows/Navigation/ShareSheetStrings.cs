namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>RU/EN strings for the Share sheet (share-sheet-ux parity with web).</summary>
public static class ShareSheetStrings
{
    public static bool IsRussian(string? uiLanguage) => ShareAccountStrings.IsRussian(uiLanguage);

    public static string ShareButton(string? lang) =>
        IsRussian(lang) ? "Поделиться" : "Share";

    public static string SheetTitle(string? lang, string entityName) =>
        IsRussian(lang) ? $"Доступ · {entityName}" : $"Share “{entityName}”";

    public static string LinkSection(string? lang) =>
        IsRussian(lang) ? "ССЫЛКА" : "LINK";

    public static string LinkHelper(string? lang) =>
        IsRussian(lang)
            ? "Откроется только у тех, у кого уже есть доступ."
            : "Opens only for people who already have access.";

    public static string CopyLink(string? lang) =>
        IsRussian(lang) ? "Копировать ссылку" : "Copy link";

    public static string GuestLinksSection(string? lang) =>
        IsRussian(lang) ? "ГОСТЕВЫЕ ССЫЛКИ" : "GUEST LINKS";

    public static string GuestLinksHelper(string? lang) =>
        IsRussian(lang)
            ? "Любой, у кого есть ссылка, сможет посмотреть — без входа в аккаунт."
            : "Anyone with a guest link can view — no account needed.";

    public static string SubtreeLine(string? lang) =>
        IsRussian(lang)
            ? "Действует для этой папки и всего, что будет добавлено позже."
            : "Covers this folder and everything added to it later.";

    public static string CreateGuestLink(string? lang) =>
        IsRussian(lang) ? "+ Создать гостевую ссылку" : "+ Create guest link";

    public static string NoGuestLinksYet(string? lang) =>
        IsRussian(lang)
            ? "Гостевых ссылок пока нет. Создайте ссылку, чтобы показать это без входа в аккаунт."
            : "No guest links yet. Create one to let someone view this without an account.";

    public static string TurnOff(string? lang) =>
        IsRussian(lang) ? "Отключить" : "Turn off";

    public static string TurnOffConfirm(string? lang) =>
        IsRussian(lang)
            ? "Отключить эту ссылку? У тех, у кого она открыта, доступ пропадёт в течение 15 минут."
            : "Turn off this link? People who have it open lose access within 15 minutes.";

    public static string Cancel(string? lang) =>
        IsRussian(lang) ? "Отмена" : "Cancel";

    public static string CreateLink(string? lang) =>
        IsRussian(lang) ? "Создать ссылку" : "Create link";

    public static string NameOptional(string? lang) =>
        IsRussian(lang) ? "Название (необязательно)" : "Name (optional)";

    public static string NamePlaceholder(string? lang) =>
        IsRussian(lang) ? "Например: Для подрядчика" : "Add a name, e.g. For contractor";

    public static string Expires(string? lang) =>
        IsRussian(lang) ? "Срок" : "Expires";

    public static string Expiry1Day(string? lang) => IsRussian(lang) ? "1 день" : "1 day";
    public static string Expiry7Days(string? lang) => IsRussian(lang) ? "7 дней" : "7 days";
    public static string Expiry30Days(string? lang) => IsRussian(lang) ? "30 дней" : "30 days";
    public static string ExpiryNever(string? lang) => IsRussian(lang) ? "Никогда" : "Never";

    public static string NeverExpiresHint(string? lang) =>
        IsRussian(lang)
            ? "Ссылка действует, пока её не отключат."
            : "The link works until someone turns it off.";

    public static string GuestLinkCreated(string? lang) =>
        IsRussian(lang) ? "Гостевая ссылка создана" : "Guest link created";

    public static string CopyOnceHint(string? lang) =>
        IsRussian(lang)
            ? "Скопируйте её сейчас — из соображений безопасности мы показываем её только один раз."
            : "Copy it now — for safety we only show it once.";

    public static string LostLinkHint(string? lang) =>
        IsRussian(lang)
            ? "Потеряли? Создайте новую — эта продолжит работать."
            : "Lost it? Create a new one; this one keeps working.";

    public static string Done(string? lang) =>
        IsRussian(lang) ? "Готово" : "Done";

    public static string Copied(string? lang) => ShareAccountStrings.CopiedLabel(lang);

    public static string CloseWithoutCopy(string? lang) =>
        IsRussian(lang)
            ? "Вы не скопировали ссылку. Она больше не будет показана."
            : "You haven't copied the link. It won't be shown again.";

    public static string CopyAndClose(string? lang) =>
        IsRussian(lang) ? "Скопировать и закрыть" : "Copy and close";

    public static string CloseAnyway(string? lang) =>
        IsRussian(lang) ? "Закрыть всё равно" : "Close anyway";

    public static string PeopleSection(string? lang) =>
        IsRussian(lang) ? "У КОГО ЕСТЬ ДОСТУП" : "PEOPLE WITH ACCESS";

    public static string CanView(string? lang) =>
        IsRussian(lang) ? "Просмотр" : "Can view";

    public static string CanEdit(string? lang) =>
        IsRussian(lang) ? "Редактирование" : "Can edit";

    public static string AnyoneOnInternet(string? lang) =>
        IsRussian(lang) ? "Любой в интернете" : "Anyone on the internet";

    public static string FooterLag(string? lang) =>
        IsRussian(lang)
            ? "Отключение доступа занимает до 15 минут у тех, у кого оно открыто. Уже скачанные файлы остаются у них."
            : "Turning off access takes up to 15 minutes for someone who has it open. Files already downloaded stay with them.";

    public static string OfflineBanner(string? lang) =>
        IsRussian(lang) ? "Ожидание сети…" : "Waiting for network…";

    public static string OfflineMutationTooltip(string? lang) =>
        IsRussian(lang) ? "Доступно, когда появится сеть" : "Available when you're back online";

    public static string ViewerOnlyFooter(string? lang) =>
        IsRussian(lang)
            ? "Делиться могут только те, у кого есть право редактирования."
            : "Only people who can edit can share this.";

    public static string LinkLimit(string? lang) =>
        IsRussian(lang)
            ? "Достигнут предел — 20 гостевых ссылок. Отключите одну, чтобы создать новую."
            : "You've reached 20 guest links. Turn one off to create another.";

    public static string NeverOpened(string? lang) =>
        IsRussian(lang) ? "Никогда не открывалась" : "Never opened";

    public static string CreatedBy(string? lang, string name) =>
        IsRussian(lang) ? $"Создал(а) {name}" : $"Created by {name}";

    public static string LinkEnding(string? lang, string prefix) =>
        IsRussian(lang) ? $"Ссылка …{prefix}" : $"Link ending …{prefix}";

    public static string InactiveLinks(string? lang, int count) =>
        IsRussian(lang) ? $"{count} неактивных ссылок" : $"{count} inactive links";

    public static string CreateFailed(string? lang) =>
        IsRussian(lang) ? "Не удалось создать ссылку. Повторите." : "Couldn't create the link. Try again.";

    public static string RevokeFailed(string? lang) =>
        IsRussian(lang) ? "Не удалось отключить ссылку. Повторите." : "Couldn't turn off the link. Try again.";

    public static string LoadFailed(string? lang) =>
        IsRussian(lang) ? "Не удалось загрузить доступ. Повторите." : "Couldn't load sharing. Try again.";

    public static string GuestLinkTurnedOff(string? lang) =>
        IsRussian(lang) ? "Гостевая ссылка отключена" : "Guest link turned off";

    public static string SendInMessenger(string? lang) =>
        IsRussian(lang) ? "Отправить в Мессенджере" : "Send in Messenger";
}
