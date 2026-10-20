using System.Windows.Input;
using ProtoLink.Communicator.Windows.Models.Share;
using ProtoLink.Communicator.Windows.Navigation;

namespace ProtoLink.Communicator.Windows.ViewModels.Share;

public sealed class SharePersonItemViewModel : ViewModelBase
{
    public SharePersonItemViewModel(
        ShareGrantResult grant,
        string? uiLanguage,
        bool isCreator,
        Func<SharePersonItemViewModel, Task> onRemove,
        Func<bool> canMutate)
    {
        Grant = grant;
        UiLanguage = uiLanguage;
        IsCreator = isCreator;
        RemoveCommand = new RelayCommand(
            async _ => await onRemove(this),
            _ => canMutate() && CanRemove && !IsCreator);
    }

    public ShareGrantResult Grant { get; }
    public string? UiLanguage { get; }
    public bool IsCreator { get; }
    public ICommand RemoveCommand { get; }

    public bool CanRemove =>
        !string.Equals(Grant.PrincipalKind, "guest_link", StringComparison.OrdinalIgnoreCase);

    public string DisplayName
    {
        get
        {
            if (string.Equals(Grant.PrincipalKind, "public", StringComparison.OrdinalIgnoreCase))
                return ShareSheetStrings.AnyoneOnInternet(UiLanguage);
            return string.IsNullOrWhiteSpace(Grant.DisplayName) ? "—" : Grant.DisplayName;
        }
    }

    public string AccessLabel =>
        Grant.CanWrite
            ? ShareSheetStrings.CanEdit(UiLanguage)
            : ShareSheetStrings.CanView(UiLanguage);

    public string? CreatorHint
    {
        get
        {
            if (!IsCreator) return null;
            return ShareSheetStrings.IsRussian(UiLanguage)
                ? "Создал(а) этот элемент"
                : "Created this item";
        }
    }
}
