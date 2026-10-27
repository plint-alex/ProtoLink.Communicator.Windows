namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>RU/EN strings for the Share sheet (share-sheet-ux parity with web).</summary>
public static class ShareSheetStrings
{
    public static bool IsRussian(string? uiLanguage) => ShareAccountStrings.IsRussian(uiLanguage);

    public static string ShareButton(string? lang) =>
        IsRussian(lang) ? "Поделиться" : "Share";

    /// <summary>Notes/Cloud context menu (UX-NS-1).</summary>
    public static string ShareMenu(string? lang) =>
        IsRussian(lang) ? "Поделиться…" : "Share…";

    public static string NotInCloudYet(string? lang) =>
        IsRussian(lang) ? "Ещё не в облаке" : "Not in Cloud yet";

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

    /// <summary>AddPermission / Add people mutation failure (not sheet List/ListGrants).</summary>
    public static string AddFailed(string? lang) =>
        IsRussian(lang) ? "Не удалось добавить. Повторите." : "Couldn't add this. Try again.";

    /// <summary>Soft status when Messenger contacts picker fails; must not replace a successful sheet load banner.</summary>
    public static string ContactsLoadSoft(string? lang) =>
        IsRussian(lang) ? "Не удалось загрузить контакты." : "Couldn't load contacts.";

    public static string GuestLinkTurnedOff(string? lang) =>
        IsRussian(lang) ? "Гостевая ссылка отключена" : "Guest link turned off";

    public static string SendInMessenger(string? lang) =>
        IsRussian(lang) ? "Отправить в Мессенджере" : "Send in Messenger";


    public static string AddPeopleButton(string? lang) =>
        IsRussian(lang) ? "+ Добавить людей" : "+ Add people";

    public static string AddPeopleTitle(string? lang) =>
        IsRussian(lang) ? "Добавить людей" : "Add people";

    public static string SearchContacts(string? lang) =>
        IsRussian(lang) ? "Поиск по контактам…" : "Search your contacts…";

    public static string PasteIdHint(string? lang) =>
        IsRussian(lang) ? "Нет в контактах? Вставьте их ID пользователя" : "Not in your contacts? Paste their user ID";

    public static string PasteIdPlaceholder(string? lang) =>
        IsRussian(lang) ? "ID пользователя или ссылка /{id}" : "User ID or /{id} link";

    public static string NoUserWithId(string? lang) =>
        IsRussian(lang) ? "Нет пользователя ProtoLink с таким ID" : "No ProtoLink user with this ID";

    public static string CanEditHelper(string? lang) =>
        IsRussian(lang)
            ? "Редактирование также позволяет делиться и отключать ссылки."
            : "Can edit also lets them share and turn off links.";

    public static string Access(string? lang) =>
        IsRussian(lang) ? "Доступ" : "Access";

    public static string Add(string? lang) =>
        IsRussian(lang) ? "Добавить" : "Add";

    public static string AddedToast(string? lang, string name, bool canWrite)
    {
        var level = canWrite
            ? (IsRussian(lang) ? "редактировать" : "edit")
            : (IsRussian(lang) ? "смотреть" : "view");
        return IsRussian(lang)
            ? $"{name} теперь может {level}. Отправьте ссылку, чтобы найти."
            : $"{name} can now {level}. Send them the link so they can find it.";
    }

    public static string NoContactsYet(string? lang) =>
        IsRussian(lang) ? "Пока нет контактов в Мессенджере." : "No Messenger contacts yet.";
    // --- Shared with me (UX §7 / AT-S17 / AT-S42) ---

    public static string SharedWithMe(string? lang) =>
        IsRussian(lang) ? "Доступные мне" : "Shared with me";

    /// <summary>Outbound inventory hub (AT-S53 / AC-SO-1).</summary>
    public static string SharedByMe(string? lang) =>
        IsRussian(lang) ? "Я поделился" : "Shared by me";

    public static string MyCloud(string? lang) =>
        IsRussian(lang) ? "Моё облако" : "My Cloud";

    public static string Recent(string? lang) =>
        IsRussian(lang) ? "Недавние" : "Recent";

    /// <summary>Cloud hub row → Photos library folder (E7 / AT-PC).</summary>
    public static string Photos(string? lang) =>
        IsRussian(lang) ? "Фото" : "Photos";

    public static string FoldersTree(string? lang) =>
        IsRussian(lang) ? "Папки" : "Folders";

    public static string CloudHub(string? lang) =>
        IsRussian(lang) ? "Облако" : "Cloud";

    public static string EmptySharedTitle(string? lang) =>
        IsRussian(lang) ? "Вам пока ничего не открыли" : "Nothing shared with you yet";

    public static string EmptySharedBody(string? lang) =>
        IsRussian(lang)
            ? "Когда кто-то даст вам доступ, это появится здесь."
            : "When someone gives you access, it shows up here.";

    public static string EmptySharedByMeTitle(string? lang) =>
        IsRussian(lang) ? "Пока ничем не поделились" : "Nothing shared yet";

    public static string EmptySharedByMeBody(string? lang) =>
        IsRussian(lang)
            ? "Когда вы откроете доступ, это появится здесь."
            : "When you give someone access, it shows up here.";

    public static string SharedByMeLoadFailed(string? lang) =>
        IsRussian(lang) ? "Не удалось загрузить «Я поделился»." : "Couldn't load Shared by me.";

    public static string EmptyRecentTitle(string? lang) =>
        IsRussian(lang) ? "Пока нет недавних" : "No recent items yet";

    public static string EmptyRecentBody(string? lang) =>
        IsRussian(lang)
            ? "Откройте файл или папку в Облаке — они появятся здесь."
            : "Open a file or folder in Cloud — it will show up here.";

    public static string EmptyFolderTitle(string? lang) =>
        IsRussian(lang) ? "Папка пуста" : "This folder is empty";

    public static string EmptyFolderBody(string? lang) =>
        IsRussian(lang)
            ? "Загрузите файл или создайте папку, чтобы начать."
            : "Upload a file or create a folder to get started.";

    public static string SharedBy(string? lang, string name) =>
        IsRussian(lang) ? $"Поделился(ась) {name}" : $"Shared by {name}";

    public static string SharedRowSubtitle(string? lang, string creatorName, bool canWrite)
    {
        var by = SharedBy(lang, creatorName);
        var level = canWrite ? CanEdit(lang) : CanView(lang);
        return $"{by} · {level}";
    }

    public static string BrowseOnlyHint(string? lang) =>
        IsRussian(lang)
            ? "Только просмотр — синхронизация с ПК для общих папок пока недоступна."
            : "Browse only — mapping shared folders to this PC is not available yet.";

    // --- Leave Shared with me (UX §7b / UX-LS-*) ---

    public static string Leave(string? lang) =>
        IsRussian(lang) ? "Покинуть" : "Leave";

    public static string LeaveMenu(string? lang) =>
        IsRussian(lang) ? "Убрать из «Доступные мне»" : "Remove from Shared with me";

    public static string LeaveConfirm(string? lang) =>
        IsRussian(lang)
            ? "Покинуть общий элемент? Доступ пропадет, пока вам снова не откроют."
            : "Leave this shared item? You lose access until someone shares it again.";

    public static string LeaveSuccess(string? lang) =>
        IsRussian(lang) ? "Убрано из «Доступные мне»" : "Removed from Shared with me";

    public static string LeaveFailed(string? lang) =>
        IsRussian(lang) ? "Не удалось покинуть. Повторите." : "Couldn't leave. Try again.";
}
