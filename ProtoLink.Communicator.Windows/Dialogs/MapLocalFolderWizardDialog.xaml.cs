using System.Collections.Generic;
using System.IO;
using System.Windows;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.Dialogs;

public sealed class MapLocalFolderCandidate
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
}

public sealed class MapLocalFolderWizardResult
{
    public required string LocalPath { get; init; }
    public bool CreateNew { get; init; }
    public string? NewFolderName { get; init; }
    public Guid? ExistingFolderId { get; init; }
    public string? ExistingFolderName { get; init; }
}

public partial class MapLocalFolderWizardDialog : Window
{
    private string _localPath;
    private readonly string? _lang;

    public MapLocalFolderWizardResult? Result { get; private set; }

    public MapLocalFolderWizardDialog(
        string? lang,
        string initialLocalPath,
        IReadOnlyList<MapLocalFolderCandidate> unmappedFolders,
        string suggestedNewFolderName)
    {
        InitializeComponent();
        _lang = lang;
        _localPath = initialLocalPath;
        Title = MapLocalFolderStrings.WizardTitle(lang);
        LocalLabel.Text = MapLocalFolderStrings.LocalFolderLabel(lang);
        ChangeLocalButton.Content = MapLocalFolderStrings.ChangeFolder(lang);
        DestinationLabel.Text = MapLocalFolderStrings.DestinationLabel(lang);
        CreateNewRadio.Content = MapLocalFolderStrings.CreateNewOption(lang);
        PickExistingRadio.Content = MapLocalFolderStrings.PickExistingOption(lang);
        FolderNameLabel.Text = MapLocalFolderStrings.FolderNameLabel(lang);
        FolderNameBox.Text = suggestedNewFolderName;
        WarnText.Text = MapLocalFolderStrings.ConfirmWarn(lang);
        ConfirmButton.Content = MapLocalFolderStrings.Confirm(lang);
        CancelButton.Content = MapLocalFolderStrings.Cancel(lang);
        LocalPathText.Text = _localPath;

        ExistingFoldersList.ItemsSource = unmappedFolders;
        if (unmappedFolders.Count == 0)
        {
            NoFoldersHint.Text = MapLocalFolderStrings.NoUnmappedFolders(lang);
            NoFoldersHint.Visibility = Visibility.Visible;
            PickExistingRadio.IsEnabled = false;
        }
        else
        {
            ExistingFoldersList.SelectedIndex = 0;
        }

        OnDestModeChanged(this, new RoutedEventArgs());
    }

    public static MapLocalFolderWizardResult? TryShow(
        string? lang,
        string? prefilledLocalPath,
        IReadOnlyList<MapLocalFolderCandidate> unmappedFolders,
        Window? owner = null)
    {
        var local = prefilledLocalPath;
        if (string.IsNullOrWhiteSpace(local) || !Directory.Exists(local))
        {
            local = FolderPicker.PickFolder(MapLocalFolderStrings.PickLocalTitle(lang), prefilledLocalPath);
            if (local is null) return null;
        }

        if (!Directory.Exists(local))
        {
            System.Windows.MessageBox.Show(
                MapLocalFolderStrings.LocalMissing(lang),
                MapLocalFolderStrings.WizardTitle(lang),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return null;
        }

        var suggested = Path.GetFileName(local.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(suggested)) suggested = "Folder";

        var dlg = new MapLocalFolderWizardDialog(lang, Path.GetFullPath(local), unmappedFolders, suggested)
        {
            Owner = owner ?? System.Windows.Application.Current?.MainWindow
        };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }

    private void OnChangeLocalClick(object sender, RoutedEventArgs e)
    {
        var path = FolderPicker.PickFolder(MapLocalFolderStrings.PickLocalTitle(_lang), _localPath);
        if (path is null) return;
        if (!Directory.Exists(path))
        {
            System.Windows.MessageBox.Show(
                MapLocalFolderStrings.LocalMissing(_lang),
                MapLocalFolderStrings.WizardTitle(_lang),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        _localPath = Path.GetFullPath(path);
        LocalPathText.Text = _localPath;
        if (string.IsNullOrWhiteSpace(FolderNameBox.Text)
            || FolderNameBox.Text == Path.GetFileName(_localPath))
        {
            FolderNameBox.Text = Path.GetFileName(
                _localPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
    }

    private void OnDestModeChanged(object sender, RoutedEventArgs e)
    {
        // Radio IsEnabled bindings handle most UI; keep confirm valid.
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_localPath))
        {
            System.Windows.MessageBox.Show(
                MapLocalFolderStrings.LocalMissing(_lang),
                MapLocalFolderStrings.WizardTitle(_lang),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (CreateNewRadio.IsChecked == true)
        {
            var name = FolderNameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                FolderNameBox.Focus();
                return;
            }

            Result = new MapLocalFolderWizardResult
            {
                LocalPath = _localPath,
                CreateNew = true,
                NewFolderName = name
            };
        }
        else
        {
            if (ExistingFoldersList.SelectedItem is not MapLocalFolderCandidate pick)
            {
                System.Windows.MessageBox.Show(
                    MapLocalFolderStrings.NoUnmappedFolders(_lang),
                    MapLocalFolderStrings.WizardTitle(_lang),
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            Result = new MapLocalFolderWizardResult
            {
                LocalPath = _localPath,
                CreateNew = false,
                ExistingFolderId = pick.Id,
                ExistingFolderName = pick.Name
            };
        }

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
