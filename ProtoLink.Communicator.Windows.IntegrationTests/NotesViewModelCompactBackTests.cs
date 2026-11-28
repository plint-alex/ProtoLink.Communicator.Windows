using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels;
using ProtoLink.Communicator.Windows.Views;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>NNB1 — compact Notes ShowBack + return-to-tree (offline).</summary>
[Trait("Category", "Offline")]
public class NotesViewModelCompactBackTests
{
    [Fact]
    public void CompactBreakpoint_Is700()
    {
        Assert.Equal(700, NotesView.CompactBreakpoint);
    }

    [Fact]
    public void CompactEditorOpen_ShowBackTrue_BackReturnsToTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "ProtoLinkNotesNNB1_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var note = Path.Combine(root, "NoteA");
            Directory.CreateDirectory(note);
            File.WriteAllText(Path.Combine(note, "index.html"), "<html><body>A</body></html>");

            var settings = new MemorySettingsService(new AppSettings { NotesRootPath = root });
            var vm = new NotesViewModel(settings);
            vm.IsCompactLayout = true;
            vm.ShowEditorInCompact = true;
            vm.RecordOpenedPage(null, note, pushHistory: false, resolveRemote: false);

            Assert.True(vm.ShowBack);
            Assert.True(vm.BackCommand.CanExecute(null));

            var returned = false;
            vm.ReturnToListRequested += () => returned = true;
            vm.BackCommand.Execute(null);

            Assert.True(returned);
            Assert.False(vm.ShowEditorInCompact);
            Assert.False(vm.ShowBack);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* NFR-TEST-1 */ }
        }
    }

    [Fact]
    public void WideNoHistory_ShowBackFalse()
    {
        var settings = new MemorySettingsService(new AppSettings());
        var vm = new NotesViewModel(settings);
        vm.IsCompactLayout = false;
        vm.ShowEditorInCompact = false;

        Assert.False(vm.ShowBack);
        Assert.False(vm.BackCommand.CanExecute(null));
    }

    [Fact]
    public void CompactWithHistory_BackPopsPriorNoteBeforeTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "ProtoLinkNotesNNB1h_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var noteA = Path.Combine(root, "NoteA");
            var noteB = Path.Combine(root, "NoteB");
            Directory.CreateDirectory(noteA);
            Directory.CreateDirectory(noteB);
            File.WriteAllText(Path.Combine(noteA, "index.html"), "<html><body>A</body></html>");
            File.WriteAllText(Path.Combine(noteB, "index.html"), "<html><body>B</body></html>");

            var settings = new MemorySettingsService(new AppSettings { NotesRootPath = root });
            var vm = new NotesViewModel(settings);
            vm.IsCompactLayout = true;
            vm.ShowEditorInCompact = true;
            vm.RecordOpenedPage(null, noteA, pushHistory: false, resolveRemote: false);
            vm.RecordOpenedPage(noteA, noteB, pushHistory: true, resolveRemote: false);

            string? navigated = null;
            vm.NavigateToNoteRequested += path => navigated = path;
            var returnedToList = false;
            vm.ReturnToListRequested += () => returnedToList = true;

            vm.BackCommand.Execute(null);

            Assert.Equal(noteA, navigated);
            Assert.False(returnedToList);
            Assert.True(vm.ShowEditorInCompact);
            Assert.True(vm.ShowBack);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* NFR-TEST-1 */ }
        }
    }

    private sealed class MemorySettingsService : ISettingsService
    {
        private AppSettings _settings;

        public MemorySettingsService(AppSettings settings) => _settings = Clone(settings);

        public AppSettings LoadSettings() => Clone(_settings);

        public void SaveSettings(AppSettings settings) => _settings = Clone(settings);

        private static AppSettings Clone(AppSettings s) => new()
        {
            Theme = s.Theme,
            ApiBaseAddress = s.ApiBaseAddress,
            PublicSiteBaseUrl = s.PublicSiteBaseUrl,
            UiLanguage = s.UiLanguage,
            NotesRootPath = s.NotesRootPath,
            NotesRootPaths = s.NotesRootPaths?.ToList() ?? new List<string>(),
            LastSelectedNotesRootPath = s.LastSelectedNotesRootPath,
            LastOpenNotesPagePath = s.LastOpenNotesPagePath,
            LastOpenCloudEntityId = s.LastOpenCloudEntityId,
        };
    }
}
