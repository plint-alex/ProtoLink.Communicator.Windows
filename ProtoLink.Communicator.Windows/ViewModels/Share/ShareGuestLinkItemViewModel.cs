using System.Windows.Input;
using ProtoLink.Communicator.Windows.Models.Share;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.ViewModels.Share;

public sealed class ShareGuestLinkItemViewModel : ViewModelBase
{
    private bool _confirmTurnOff;
    private bool _busy;

    public ShareGuestLinkItemViewModel(
        ShareLinkListItemResult link,
        string? uiLanguage,
        Func<ShareGuestLinkItemViewModel, Task> onTurnOff,
        Func<bool> canMutate)
    {
        Link = link;
        UiLanguage = uiLanguage;
        TurnOffCommand = new RelayCommand(
            async _ =>
            {
                if (!_confirmTurnOff)
                {
                    ConfirmTurnOff = true;
                    return;
                }

                if (_busy) return;
                _busy = true;
                try { await onTurnOff(this); }
                finally
                {
                    _busy = false;
                    ConfirmTurnOff = false;
                }
            },
            _ => canMutate() && !_busy);
        CancelTurnOffCommand = new RelayCommand(_ => ConfirmTurnOff = false);
    }

    public ShareLinkListItemResult Link { get; }
    public string? UiLanguage { get; }
    public Guid Id => Link.Id;
    public bool IsLive => Link.IsLive;
    public ICommand TurnOffCommand { get; }
    public ICommand CancelTurnOffCommand { get; }

    public bool ConfirmTurnOff
    {
        get => _confirmTurnOff;
        private set
        {
            if (_confirmTurnOff == value) return;
            _confirmTurnOff = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowNormalRow));
        }
    }

    public bool ShowNormalRow => !_confirmTurnOff;

    public string TitleLine
    {
        get
        {
            var prefix = Link.TokenPrefix ?? "";
            if (!string.IsNullOrWhiteSpace(Link.Label))
                return $"{Link.Label.Trim()} · …{prefix}";
            return ShareSheetStrings.LinkEnding(UiLanguage, prefix);
        }
    }

    public string ExpiryLine
    {
        get
        {
            if (Link.RevokedAt is { } revoked)
            {
                var when = ChatTime.FormatListMeta(revoked);
                return ShareSheetStrings.IsRussian(UiLanguage)
                    ? $"Отключена {when}"
                    : $"Turned off {when}";
            }

            if (Link.ExpiresAt is not { } exp)
            {
                return ShareSheetStrings.IsRussian(UiLanguage) ? "Без срока" : "Never expires";
            }

            var local = exp.Kind == DateTimeKind.Utc ? exp.ToLocalTime() : exp;
            var remaining = local - DateTime.Now;
            if (remaining.TotalHours is > 0 and < 24)
            {
                var hours = Math.Max(1, (int)Math.Ceiling(remaining.TotalHours));
                return ShareSheetStrings.IsRussian(UiLanguage)
                    ? $"Истекает через {hours} ч"
                    : $"Expires in {hours} hours";
            }

            if (remaining.TotalSeconds <= 0)
            {
                return ShareSheetStrings.IsRussian(UiLanguage)
                    ? $"Истекла {ChatTime.FormatListMeta(exp)}"
                    : $"Expired {ChatTime.FormatListMeta(exp)}";
            }

            return ShareSheetStrings.IsRussian(UiLanguage)
                ? $"Истекает {ChatTime.FormatListMeta(exp)}"
                : $"Expires {ChatTime.FormatListMeta(exp)}";
        }
    }

    public string UsageLine
    {
        get
        {
            if (Link.UseCount <= 0 || Link.LastUsedAt is null)
                return ShareSheetStrings.NeverOpened(UiLanguage);
            var n = Link.UseCount;
            var last = ChatTime.FormatListMeta(Link.LastUsedAt.Value);
            return ShareSheetStrings.IsRussian(UiLanguage)
                ? $"Открыта {n} раз, последний раз {last}"
                : $"Opened {n} times, last {last}";
        }
    }

    public string CreatedByLine =>
        ShareSheetStrings.CreatedBy(UiLanguage, string.IsNullOrWhiteSpace(Link.CreatedByDisplayName)
            ? "—"
            : Link.CreatedByDisplayName!);

    public string TurnOffLabel => ShareSheetStrings.TurnOff(UiLanguage);
    public string TurnOffConfirmText => ShareSheetStrings.TurnOffConfirm(UiLanguage);
    public string CancelLabel => ShareSheetStrings.Cancel(UiLanguage);
}
