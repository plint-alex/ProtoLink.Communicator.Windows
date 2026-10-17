using System.Windows;
using System.Windows.Input;
using ContextMenu = System.Windows.Controls.ContextMenu;

namespace ProtoLink.Communicator.Windows.Controls;

/// <summary>
/// Shared entity chrome for Cloud, Notes, and later Messenger.
/// Bindings only — no Cloud/Notes view-model types in the API.
/// </summary>
public partial class EntityHeader : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(EntityHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(EntityHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TypeGlyphProperty =
        DependencyProperty.Register(nameof(TypeGlyph), typeof(string), typeof(EntityHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ShowBackProperty =
        DependencyProperty.Register(nameof(ShowBack), typeof(bool), typeof(EntityHeader), new PropertyMetadata(false));

    public static readonly DependencyProperty ShowCopyLinkProperty =
        DependencyProperty.Register(nameof(ShowCopyLink), typeof(bool), typeof(EntityHeader), new PropertyMetadata(true));

    public static readonly DependencyProperty CopyLinkToolTipProperty =
        DependencyProperty.Register(nameof(CopyLinkToolTip), typeof(string), typeof(EntityHeader),
            new PropertyMetadata("Copy link (Ctrl+Shift+C)"));

    public static readonly DependencyProperty ShowMoreMenuProperty =
        DependencyProperty.Register(nameof(ShowMoreMenu), typeof(bool), typeof(EntityHeader), new PropertyMetadata(true));

    public static readonly DependencyProperty BackCommandProperty =
        DependencyProperty.Register(nameof(BackCommand), typeof(ICommand), typeof(EntityHeader));

    public static readonly DependencyProperty CopyLinkCommandProperty =
        DependencyProperty.Register(nameof(CopyLinkCommand), typeof(ICommand), typeof(EntityHeader));

    public static readonly DependencyProperty NavigateParentCommandProperty =
        DependencyProperty.Register(nameof(NavigateParentCommand), typeof(ICommand), typeof(EntityHeader));

    public static readonly DependencyProperty MoreMenuProperty =
        DependencyProperty.Register(nameof(MoreMenu), typeof(ContextMenu), typeof(EntityHeader));

    public EntityHeader()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Shown under the title (e.g. "… › Parent"). Click runs <see cref="NavigateParentCommand"/>.</summary>
    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string TypeGlyph
    {
        get => (string)GetValue(TypeGlyphProperty);
        set => SetValue(TypeGlyphProperty, value);
    }

    public bool ShowBack
    {
        get => (bool)GetValue(ShowBackProperty);
        set => SetValue(ShowBackProperty, value);
    }

    public bool ShowCopyLink
    {
        get => (bool)GetValue(ShowCopyLinkProperty);
        set => SetValue(ShowCopyLinkProperty, value);
    }

    /// <summary>Notes uses "Not in Cloud yet" when Copy link is disabled (E2).</summary>
    public string CopyLinkToolTip
    {
        get => (string)GetValue(CopyLinkToolTipProperty);
        set => SetValue(CopyLinkToolTipProperty, value);
    }

    public bool ShowMoreMenu
    {
        get => (bool)GetValue(ShowMoreMenuProperty);
        set => SetValue(ShowMoreMenuProperty, value);
    }

    public ICommand? BackCommand
    {
        get => (ICommand?)GetValue(BackCommandProperty);
        set => SetValue(BackCommandProperty, value);
    }

    public ICommand? CopyLinkCommand
    {
        get => (ICommand?)GetValue(CopyLinkCommandProperty);
        set => SetValue(CopyLinkCommandProperty, value);
    }

    public ICommand? NavigateParentCommand
    {
        get => (ICommand?)GetValue(NavigateParentCommandProperty);
        set => SetValue(NavigateParentCommandProperty, value);
    }

    public ContextMenu? MoreMenu
    {
        get => (ContextMenu?)GetValue(MoreMenuProperty);
        set => SetValue(MoreMenuProperty, value);
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (MoreMenu == null) return;
        MoreMenu.PlacementTarget = MoreButton;
        MoreMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        MoreMenu.IsOpen = true;
    }
}
