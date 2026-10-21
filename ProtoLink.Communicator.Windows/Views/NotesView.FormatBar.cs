using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.Views;

public partial class NotesView
{
    private double _selectionCenterX;
    private double _selectionTop;
    private double _selectionBottom;
    private bool _formatBarLabelsApplied;

    private void HandleFormatBarWebMessage(JsonElement root, string? type)
    {
        if (type == "dismissFormatBar")
        {
            Dispatcher.BeginInvoke(new Action(OnDismissFormatBarFromEditor));
            return;
        }

        if (type != "selectionChanged")
            return;

        var hasSelection = root.TryGetProperty("hasSelection", out var hasEl) && hasEl.GetBoolean();
        if (!hasSelection)
        {
            Dispatcher.BeginInvoke(new Action(HideFormatBar));
            return;
        }

        var x = root.TryGetProperty("x", out var xEl) ? xEl.GetDouble() : 0;
        var y = root.TryGetProperty("y", out var yEl) ? yEl.GetDouble() : 0;
        var bottom = root.TryGetProperty("bottom", out var bEl) ? bEl.GetDouble() : y;
        Dispatcher.BeginInvoke(new Action(() => ShowFormatBarNearSelection(x, y, bottom)));
    }

    private void OnDismissFormatBarFromEditor()
    {
        var wasOpen = FormatBarPopup.IsOpen;
        HideFormatBar();
        if (wasOpen)
            return;
        if (_vm?.IsCompactLayout == true && _vm.ShowEditorInCompact)
            _vm.ReturnToListInCompact();
    }

    private void HideFormatBar()
    {
        if (FormatBarPopup.IsOpen)
            FormatBarPopup.IsOpen = false;
    }

    private void ShowFormatBarNearSelection(double selectionCenterX, double selectionTop, double selectionBottom)
    {
        if (_vm is not { HasOpenPage: true })
        {
            HideFormatBar();
            return;
        }

        ApplyFormatBarLabelsIfNeeded();

        _selectionCenterX = selectionCenterX;
        _selectionTop = selectionTop;
        _selectionBottom = selectionBottom;

        FormatBarPopup.IsOpen = true;
        FormatBarPanel.UpdateLayout();
        PositionFormatBar();
    }

    private void PositionFormatBar()
    {
        if (!FormatBarPopup.IsOpen)
            return;

        var paneW = EditorHost.ActualWidth;
        var paneH = EditorHost.ActualHeight;
        if (paneW <= 0 || paneH <= 0)
            return;

        var barW = FormatBarPanel.ActualWidth;
        var barH = FormatBarPanel.ActualHeight;
        if (barW <= 0 || barH <= 0)
        {
            FormatBarPanel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            barW = FormatBarPanel.DesiredSize.Width;
            barH = FormatBarPanel.DesiredSize.Height;
        }

        var point = NotesFloatingFormatBarPlacement.Place(
            _selectionCenterX,
            _selectionTop,
            _selectionBottom,
            barW,
            barH,
            paneW,
            paneH);

        FormatBarPopup.HorizontalOffset = point.X;
        FormatBarPopup.VerticalOffset = point.Y;
    }

    private void ApplyFormatBarLabelsIfNeeded()
    {
        if (_formatBarLabelsApplied)
            return;

        var lang = _vm?.UiLanguage;
        FormatBoldButton.ToolTip = NotesFormatBarStrings.Bold(lang);
        AutomationProperties.SetName(FormatBoldButton, NotesFormatBarStrings.Bold(lang));
        FormatItalicButton.ToolTip = NotesFormatBarStrings.Italic(lang);
        AutomationProperties.SetName(FormatItalicButton, NotesFormatBarStrings.Italic(lang));
        FormatUnderlineButton.ToolTip = NotesFormatBarStrings.Underline(lang);
        AutomationProperties.SetName(FormatUnderlineButton, NotesFormatBarStrings.Underline(lang));
        FormatStrikeButton.ToolTip = NotesFormatBarStrings.Strikethrough(lang);
        AutomationProperties.SetName(FormatStrikeButton, NotesFormatBarStrings.Strikethrough(lang));
        HeadingCombo.ToolTip = NotesFormatBarStrings.TextStyle(lang);
        AutomationProperties.SetName(HeadingCombo, NotesFormatBarStrings.TextStyle(lang));
        HeadingNormalItem.Content = NotesFormatBarStrings.Normal(lang);
        Heading1Item.Content = NotesFormatBarStrings.Heading1(lang);
        Heading2Item.Content = NotesFormatBarStrings.Heading2(lang);
        Heading3Item.Content = NotesFormatBarStrings.Heading3(lang);
        FormatBulletButton.ToolTip = NotesFormatBarStrings.BulletList(lang);
        AutomationProperties.SetName(FormatBulletButton, NotesFormatBarStrings.BulletList(lang));
        FormatNumberedButton.ToolTip = NotesFormatBarStrings.NumberedList(lang);
        AutomationProperties.SetName(FormatNumberedButton, NotesFormatBarStrings.NumberedList(lang));
        FormatCheckboxButton.ToolTip = NotesFormatBarStrings.CheckboxList(lang);
        AutomationProperties.SetName(FormatCheckboxButton, NotesFormatBarStrings.CheckboxList(lang));
        FormatLinkButton.ToolTip = NotesFormatBarStrings.InsertLink(lang);
        AutomationProperties.SetName(FormatLinkButton, NotesFormatBarStrings.InsertLink(lang));
        FormatCodeButton.ToolTip = NotesFormatBarStrings.CodeBlock(lang);
        AutomationProperties.SetName(FormatCodeButton, NotesFormatBarStrings.CodeBlock(lang));
        FormatTableButton.ToolTip = NotesFormatBarStrings.InsertTable(lang);
        AutomationProperties.SetName(FormatTableButton, NotesFormatBarStrings.InsertTable(lang));
        _formatBarLabelsApplied = true;
    }

    private void OnEditorHostSizeChangedForFormatBar(object sender, SizeChangedEventArgs e)
    {
        if (FormatBarPopup.IsOpen)
            PositionFormatBar();
    }
}
