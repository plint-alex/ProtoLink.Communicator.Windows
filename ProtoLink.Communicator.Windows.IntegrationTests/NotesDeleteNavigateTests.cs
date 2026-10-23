using System.Text;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class NotesDeleteNavigateTests
{
    [Fact]
    public void ResolveNextPath_PrevSibling_WhenOpenLeafDeleted()
    {
        var root = CreateTempNotesRoot();
        try
        {
            var a = Path.Combine(root, "A");
            var b = Path.Combine(root, "B");
            Directory.CreateDirectory(a);
            Directory.CreateDirectory(b);
            WriteIndex(a, "A");
            WriteIndex(b, "B");

            var next = NotesDeleteNavigate.ResolveNextPath(root, b, b);
            Assert.Equal(Path.GetFullPath(a), Path.GetFullPath(next!));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveNextPath_Parent_WhenSoleChildDeleted()
    {
        var root = CreateTempNotesRoot();
        try
        {
            var parent = Path.Combine(root, "Parent");
            var child = Path.Combine(parent, "Only");
            Directory.CreateDirectory(child);
            WriteIndex(parent, "Parent");
            WriteIndex(child, "Only");

            var next = NotesDeleteNavigate.ResolveNextPath(root, child, child);
            Assert.Equal(Path.GetFullPath(parent), Path.GetFullPath(next!));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveNextPath_Root_WhenSoleChildUnderRootDeleted()
    {
        var root = CreateTempNotesRoot();
        try
        {
            var only = Path.Combine(root, "Only");
            Directory.CreateDirectory(only);
            WriteIndex(only, "Only");

            var next = NotesDeleteNavigate.ResolveNextPath(root, only, only);
            Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(next!));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveNextPath_Unchanged_WhenNonOpenSiblingDeleted()
    {
        var root = CreateTempNotesRoot();
        try
        {
            var a = Path.Combine(root, "A");
            var b = Path.Combine(root, "B");
            Directory.CreateDirectory(a);
            Directory.CreateDirectory(b);
            WriteIndex(a, "A");
            WriteIndex(b, "B");

            var next = NotesDeleteNavigate.ResolveNextPath(root, a, b);
            Assert.Equal(Path.GetFullPath(b), Path.GetFullPath(next!));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveNextPath_AncestorDelete_OpensPrevSiblingOfAncestor()
    {
        var root = CreateTempNotesRoot();
        try
        {
            var a = Path.Combine(root, "A");
            var b = Path.Combine(root, "B");
            var nested = Path.Combine(b, "Nested");
            Directory.CreateDirectory(a);
            Directory.CreateDirectory(nested);
            WriteIndex(a, "A");
            WriteIndex(b, "B");
            WriteIndex(nested, "Nested");

            var next = NotesDeleteNavigate.ResolveNextPath(root, b, nested);
            Assert.Equal(Path.GetFullPath(a), Path.GetFullPath(next!));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveNextPath_FirstSibling_UsesNextAsNearest()
    {
        var root = CreateTempNotesRoot();
        try
        {
            var a = Path.Combine(root, "A");
            var b = Path.Combine(root, "B");
            Directory.CreateDirectory(a);
            Directory.CreateDirectory(b);
            WriteIndex(a, "A");
            WriteIndex(b, "B");

            var next = NotesDeleteNavigate.ResolveNextPath(root, a, a);
            Assert.Equal(Path.GetFullPath(b), Path.GetFullPath(next!));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void NotesViewModel_DeleteFolder_SetsCurrentToPrevSibling()
    {
        var root = CreateTempNotesRoot();
        try
        {
            var a = Path.Combine(root, "A");
            var b = Path.Combine(root, "B");
            Directory.CreateDirectory(a);
            Directory.CreateDirectory(b);
            WriteIndex(a, "A");
            WriteIndex(b, "B");

            var settings = new MemorySettingsService(new AppSettings { NotesRootPath = root });
            var vm = new NotesViewModel(settings);
            vm.CurrentPagePath = b;

            Assert.True(vm.DeleteFolder(b));
            Assert.Equal(Path.GetFullPath(a), Path.GetFullPath(vm.CurrentPagePath!));
            Assert.Equal("Deleted", vm.StatusText);
            Assert.False(Directory.Exists(b));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void NotesViewModel_DeleteFolder_LeavesOpenPageWhenOtherSiblingDeleted()
    {
        var root = CreateTempNotesRoot();
        try
        {
            var a = Path.Combine(root, "A");
            var b = Path.Combine(root, "B");
            Directory.CreateDirectory(a);
            Directory.CreateDirectory(b);
            WriteIndex(a, "A");
            WriteIndex(b, "B");

            var settings = new MemorySettingsService(new AppSettings { NotesRootPath = root });
            var vm = new NotesViewModel(settings);
            vm.CurrentPagePath = b;

            Assert.True(vm.DeleteFolder(a));
            Assert.Equal(Path.GetFullPath(b), Path.GetFullPath(vm.CurrentPagePath!));
            Assert.True(Directory.Exists(b));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void WriteIndex(string folder, string title) =>
        File.WriteAllText(Path.Combine(folder, "index.html"), $"<h1>{title}</h1>", Encoding.UTF8);

    private static string CreateTempNotesRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ProtoLinkNotesDeleteNav_" + Guid.NewGuid().ToString("N"));
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
            // best-effort temp cleanup
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
