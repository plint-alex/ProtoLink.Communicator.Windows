using System.Text;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class NotesFolderMoveOfflineTests
{
    [Fact]
    public void ValidateMove_RejectsRootSelfDescendantAndCollision()
    {
        var root = CreateTempNotesRoot();
        try
        {
            var a = Path.Combine(root, "A");
            var b = Path.Combine(root, "B");
            var child = Path.Combine(a, "Child");
            Directory.CreateDirectory(child);
            Directory.CreateDirectory(b);
            File.WriteAllText(Path.Combine(a, "index.html"), "<h1>A</h1>", Encoding.UTF8);
            File.WriteAllText(Path.Combine(b, "index.html"), "<h1>B</h1>", Encoding.UTF8);
            File.WriteAllText(Path.Combine(child, "index.html"), "<h1>Child</h1>", Encoding.UTF8);
            Directory.CreateDirectory(Path.Combine(b, "A"));

            Assert.Equal(
                "Cannot move the notes root folder.",
                NotesFileSystemService.ValidateMove(root, b, root));
            Assert.Equal(
                "Cannot drop a page onto itself.",
                NotesFileSystemService.ValidateMove(a, a, root));
            Assert.Equal(
                "Cannot move a page into its own subpage.",
                NotesFileSystemService.ValidateMove(a, child, root));
            Assert.Equal(
                "A page named 'A' already exists there.",
                NotesFileSystemService.ValidateMove(a, b, root));
            Assert.Equal(
                "Already in that folder.",
                NotesFileSystemService.ValidateMove(a, root, root));
            Assert.Null(NotesFileSystemService.ValidateMove(child, b, root));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void MoveFolder_ReparentsOnDisk()
    {
        var root = CreateTempNotesRoot();
        try
        {
            var source = Path.Combine(root, "Page");
            var target = Path.Combine(root, "Target");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(source, "index.html"), "<h1>Page</h1>", Encoding.UTF8);

            var fs = new NotesFileSystemService();
            var dest = fs.MoveFolder(source, target);

            Assert.Equal(Path.Combine(target, "Page"), dest);
            Assert.True(Directory.Exists(dest));
            Assert.False(Directory.Exists(source));
            Assert.True(File.Exists(Path.Combine(dest, "index.html")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void NotesViewModel_MoveFolder_UpdatesTreeAndCurrentPath()
    {
        var root = CreateTempNotesRoot();
        try
        {
            var source = Path.Combine(root, "MoveMe");
            var target = Path.Combine(root, "Dest");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(source, "index.html"), "<h1>MoveMe</h1>", Encoding.UTF8);
            File.WriteAllText(Path.Combine(target, "index.html"), "<h1>Dest</h1>", Encoding.UTF8);

            var settings = new MemorySettingsService(new AppSettings { NotesRootPath = root });
            var vm = new NotesViewModel(settings);
            vm.CurrentPagePath = source;

            Assert.True(vm.MoveFolder(source, target));
            var expected = Path.Combine(target, "MoveMe");
            Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(vm.CurrentPagePath!));
            Assert.Equal("Moved", vm.StatusText);
            Assert.False(Directory.Exists(source));
            Assert.True(Directory.Exists(expected));

            Assert.NotNull(vm.TreeRoot);
            var destNode = Assert.Single(vm.TreeRoot!.Children, c => c.Name == "Dest");
            Assert.Contains(destNode.Children, c => c.Name == "MoveMe");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void NotesViewModel_MoveFolder_RejectsCollisionWithStatusText()
    {
        var root = CreateTempNotesRoot();
        try
        {
            var a = Path.Combine(root, "Same");
            var parent = Path.Combine(root, "Parent");
            Directory.CreateDirectory(a);
            Directory.CreateDirectory(Path.Combine(parent, "Same"));
            File.WriteAllText(Path.Combine(a, "index.html"), "<h1>Same</h1>", Encoding.UTF8);

            var settings = new MemorySettingsService(new AppSettings { NotesRootPath = root });
            var vm = new NotesViewModel(settings);

            Assert.False(vm.MoveFolder(a, parent));
            Assert.Equal("A page named 'Same' already exists there.", vm.StatusText);
            Assert.True(Directory.Exists(a));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateTempNotesRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ProtoLinkNotesMove_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
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
            LastOpenCloudEntityId = s.LastOpenCloudEntityId
        };
    }
}
