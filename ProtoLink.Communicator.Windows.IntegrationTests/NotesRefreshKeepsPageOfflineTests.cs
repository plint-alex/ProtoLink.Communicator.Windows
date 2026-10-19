using System.Text;
using System.Windows;
using System.Windows.Threading;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Regression for Notes tree refresh jumping the open page to root (Windows 1.0.7).
/// VM must keep CurrentPagePath across RefreshTree; UI suppress is in NotesView.PopulateTree.
/// </summary>
[Trait("Category", "Offline")]
public class NotesRefreshKeepsPageOfflineTests
{
    [Fact]
    public void RefreshTree_KeepsCurrentPagePath()
    {
        var root = CreateTempNotesRoot();
        try
        {
            var nested = Path.Combine(root, "Parent", "Child");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(root, "index.html"), "<h1>Root</h1>", Encoding.UTF8);
            File.WriteAllText(Path.Combine(root, "Parent", "index.html"), "<h1>Parent</h1>", Encoding.UTF8);
            File.WriteAllText(Path.Combine(nested, "index.html"), "<h1>Child</h1>", Encoding.UTF8);

            var settings = new MemorySettingsService(new AppSettings { NotesRootPath = root });
            var vm = new NotesViewModel(settings);
            vm.CurrentPagePath = nested;

            File.WriteAllText(Path.Combine(nested, "index.html"), "<h1>Child v2</h1>", Encoding.UTF8);
            vm.RefreshTree();
            PumpDispatcherUntil(() => vm.TreeRoot != null, TimeSpan.FromSeconds(5));

            Assert.Equal(Path.GetFullPath(nested), Path.GetFullPath(vm.CurrentPagePath!));
            Assert.NotNull(vm.TreeRoot);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void PumpDispatcherUntil(Func<bool> ready, TimeSpan timeout)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            Assert.True(ready());
            return;
        }

        var deadline = DateTime.UtcNow + timeout;
        while (!ready() && DateTime.UtcNow < deadline)
            dispatcher.Invoke(DispatcherPriority.Background, static () => { });
        Assert.True(ready(), "Timed out waiting for tree refresh.");
    }

    private static string CreateTempNotesRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ProtoLinkNotesRefresh_" + Guid.NewGuid().ToString("N"));
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
            // best-effort cleanup
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
