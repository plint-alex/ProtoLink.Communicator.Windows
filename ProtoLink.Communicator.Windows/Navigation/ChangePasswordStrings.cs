namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>Change-password copy + client validation (docs/features/change-password; same rules as web Register min 5).</summary>
public static class ChangePasswordStrings
{
    public const int MinPasswordLength = 5;

    public static string Title(string? uiLanguage) =>
        ShareAccountStrings.IsRussian(uiLanguage) ? "Смена пароля" : "Change password";

    public static string MenuLabel(string? uiLanguage) =>
        ShareAccountStrings.IsRussian(uiLanguage) ? "Сменить пароль" : "Change password";

    public static string FieldCurrent(string? uiLanguage) =>
        ShareAccountStrings.IsRussian(uiLanguage) ? "Текущий пароль" : "Current password";

    public static string FieldNew(string? uiLanguage) =>
        ShareAccountStrings.IsRussian(uiLanguage) ? "Новый пароль" : "New password";

    public static string FieldConfirm(string? uiLanguage) =>
        ShareAccountStrings.IsRussian(uiLanguage) ? "Подтвердите новый пароль" : "Confirm new password";

    public static string Submit(string? uiLanguage) =>
        ShareAccountStrings.IsRussian(uiLanguage) ? "Сохранить пароль" : "Save password";

    public static string Cancel(string? uiLanguage) =>
        ShareAccountStrings.IsRussian(uiLanguage) ? "Отмена" : "Cancel";

    public static string Success(string? uiLanguage) =>
        ShareAccountStrings.IsRussian(uiLanguage) ? "Пароль обновлён." : "Password updated.";

    public static string SuccessHint(string? uiLanguage) =>
        ShareAccountStrings.IsRussian(uiLanguage)
            ? "На других устройствах может потребоваться повторный вход."
            : "Other devices may need to sign in again.";

    public static string ErrorCurrent(string? uiLanguage) =>
        ShareAccountStrings.IsRussian(uiLanguage)
            ? "Неверный текущий пароль."
            : "Current password is incorrect.";

    public static string ValidationRequired(string? uiLanguage) =>
        ShareAccountStrings.IsRussian(uiLanguage) ? "Обязательное поле" : "Required";

    public static string ValidationMin(string? uiLanguage) =>
        ShareAccountStrings.IsRussian(uiLanguage)
            ? "Пароль не короче 5 символов"
            : "Password must be at least 5 characters";

    public static string ValidationMismatch(string? uiLanguage) =>
        ShareAccountStrings.IsRussian(uiLanguage) ? "Пароли не совпадают" : "Passwords do not match";

    /// <summary>Client-side validation before Api call. Null = OK.</summary>
    public static string? Validate(string? currentPassword, string? newPassword, string? confirmPassword, string? uiLanguage)
    {
        if (string.IsNullOrEmpty(currentPassword))
            return ValidationRequired(uiLanguage);
        if (string.IsNullOrEmpty(newPassword))
            return ValidationRequired(uiLanguage);
        if (string.IsNullOrEmpty(confirmPassword))
            return ValidationRequired(uiLanguage);
        if (newPassword.Length < MinPasswordLength)
            return ValidationMin(uiLanguage);
        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            return ValidationMismatch(uiLanguage);
        return null;
    }
}
