using System.Linq;
using System.Windows;
using System.Windows.Input;
using ProtoLink.Communicator.Windows.Services;

namespace ProtoLink.Communicator.Windows.Dialogs;

public partial class ErrorDetailDialog : Window
{
    public ErrorDetailDialog()
    {
        InitializeComponent();
    }

    public string ErrorText
    {
        get => ErrorTextBox.Text;
        set => ErrorTextBox.Text = value ?? "";
    }

    /// <summary>Shows a modeless window so the main window stays usable and the app can be closed.</summary>
    public static void Show(string title, Exception ex)
    {
        if (ApiExceptionHelper.IndicatesUnauthorizedHttp(ex))
            return;
        Show(title, ex.ToString());
    }

    /// <summary>Shows a modeless window so the main window stays usable and the app can be closed.</summary>
    public static void Show(string title, string fullErrorText)
    {
        void ShowCore()
        {
            if (System.Windows.Application.Current.Windows.OfType<ErrorDetailDialog>().Any(w => w.IsVisible))
                return;

            var dlg = new ErrorDetailDialog
            {
                Owner = System.Windows.Application.Current.MainWindow,
                Title = title,
                ErrorText = fullErrorText
            };
            dlg.Show();
            dlg.Activate();
        }

        var disp = System.Windows.Application.Current.Dispatcher;
        if (disp.CheckAccess())
            ShowCore();
        else
            disp.BeginInvoke(ShowCore);
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(ErrorTextBox.Text);
        }
        catch { }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
