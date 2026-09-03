using System.Windows;
using System.Windows.Input;

namespace ProtoLink.Communicator.Windows.Dialogs;

public partial class InputDialog : Window
{
    private string? _result;

    public InputDialog()
    {
        InitializeComponent();
    }

    public string Prompt { get => PromptTextBlock.Text; set => PromptTextBlock.Text = value; }
    public string Input { get => InputTextBox.Text; set => InputTextBox.Text = value; }

    public static bool TryShow(string prompt, string title, out string? result)
    {
        return TryShow(prompt, title, initialInput: string.Empty, out result);
    }

    public static bool TryShow(string prompt, string title, string initialInput, out string? result)
    {
        var dlg = new InputDialog
        {
            Owner = System.Windows.Application.Current.MainWindow,
            Title = title,
            Prompt = prompt,
            Input = initialInput
        };
        if (dlg.ShowDialog() == true)
        {
            result = dlg._result;
            return true;
        }
        result = null;
        return false;
    }

    private void CommitAndClose()
    {
        _result = InputTextBox?.Text?.Trim();
        DialogResult = true;
        Close();
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitAndClose();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        CommitAndClose();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
