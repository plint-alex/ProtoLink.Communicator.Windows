using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Client validation + RU/EN copy for Settings Change password (docs/features/change-password).</summary>
[Trait("Category", "Offline")]
public class ChangePasswordStringsTests
{
    [Fact]
    public void Validate_RejectsEmptyFields()
    {
        Assert.Equal(
            ChangePasswordStrings.ValidationRequired("en-US"),
            ChangePasswordStrings.Validate(null, "abcde", "abcde", "en-US"));
        Assert.Equal(
            ChangePasswordStrings.ValidationRequired("en-US"),
            ChangePasswordStrings.Validate("old", "", "abcde", "en-US"));
        Assert.Equal(
            ChangePasswordStrings.ValidationRequired("en-US"),
            ChangePasswordStrings.Validate("old", "abcde", "", "en-US"));
    }

    [Fact]
    public void Validate_RejectsShortNewPassword()
    {
        Assert.Equal(
            ChangePasswordStrings.ValidationMin("en-US"),
            ChangePasswordStrings.Validate("old", "abcd", "abcd", "en-US"));
        Assert.Equal(
            ChangePasswordStrings.ValidationMin("ru-RU"),
            ChangePasswordStrings.Validate("old", "12", "12", "ru-RU"));
    }

    [Fact]
    public void Validate_RejectsMismatch()
    {
        Assert.Equal(
            ChangePasswordStrings.ValidationMismatch("en-US"),
            ChangePasswordStrings.Validate("old", "abcde", "abcde!", "en-US"));
    }

    [Fact]
    public void Validate_OkWhenMinLengthAndMatch()
    {
        Assert.Null(ChangePasswordStrings.Validate("old", "abcde", "abcde", "en-US"));
    }

    [Fact]
    public void MenuAndTitle_UseRussianWhenUiLanguageRu()
    {
        Assert.Equal("Сменить пароль", ChangePasswordStrings.MenuLabel("ru-RU"));
        Assert.Equal("Смена пароля", ChangePasswordStrings.Title("ru-RU"));
        Assert.Equal("Change password", ChangePasswordStrings.MenuLabel("en-US"));
    }
}
