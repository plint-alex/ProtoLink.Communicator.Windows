using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;
using ProtoLink.Communicator.Windows.Views;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Installed UIA verification selects Notes pages by name. The main TabControl template must use
/// PART_SelectedContentHost, and Notes tree rows must expose AutomationProperties.Name.
/// Avoids constructing NotesView / Application.Current (a leftover App.Dispatcher breaks NotesViewModel.RefreshTree).
/// </summary>
[Trait("Category", "Offline")]
public class NotesTreeAutomationPeerTests
{
    private const string MainTabStyleKey = "TabControlWithSettingsButton";

    [Fact]
    public void NotesTab_TreeItems_AreReachableByName()
    {
        RunOnSta(() =>
        {
            Assert.Null(Application.Current);

            var tree = new TreeView { Name = "NotesTreeView" };
            var parent = new TreeViewItem();
            NotesView.ApplyTreeItemAutomationName(parent, "Scratch parent");
            parent.IsExpanded = true;
            var child = new TreeViewItem();
            NotesView.ApplyTreeItemAutomationName(child, "Scratch child");
            parent.Items.Add(child);
            tree.Items.Add(parent);

            var tabs = new TabControl { Style = LoadMainTabStyle() };
            tabs.Items.Add(new TabItem { Header = "Messenger", Content = new Grid() });
            tabs.Items.Add(new TabItem { Header = "Notes", Content = tree });
            tabs.SelectedIndex = 1;
            tabs.Measure(new Size(900, 600));
            tabs.Arrange(new Rect(0, 0, 900, 600));
            tabs.UpdateLayout();

            var tabsPeer = UIElementAutomationPeer.CreatePeerForElement(tabs);
            var treePeer = FindDescendant(tabsPeer, p => p.GetAutomationId() == "NotesTreeView");
            Assert.NotNull(treePeer);
            Assert.Equal(AutomationControlType.Tree, treePeer.GetAutomationControlType());

            foreach (var title in new[] { "Scratch parent", "Scratch child" })
            {
                var item = FindDescendant(treePeer, p =>
                    p.GetAutomationControlType() == AutomationControlType.TreeItem && p.GetName() == title);
                Assert.True(item != null, $"TreeItem '{title}' not reachable through UIA peers.");
                Assert.NotNull(item.GetPattern(PatternInterface.ExpandCollapse));
                Assert.NotNull(item.GetPattern(PatternInterface.SelectionItem));
            }

            Assert.Null(Application.Current);
        });
    }

    private static Style LoadMainTabStyle()
    {
        var mainWindowXaml = Path.Combine(RepoRoot(), "ProtoLink.Communicator.Windows", "MainWindow.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var styleElement = XDocument.Load(mainWindowXaml)
            .Descendants()
            .Single(e => e.Name.LocalName == "Style" && (string?)e.Attribute(x + "Key") == MainTabStyleKey);
        return (Style)XamlReader.Parse(styleElement.ToString());
    }

    private static AutomationPeer? FindDescendant(AutomationPeer peer, Func<AutomationPeer, bool> match)
    {
        foreach (var child in peer.GetChildren() ?? [])
        {
            if (match(child))
                return child;
            var found = FindDescendant(child, match);
            if (found != null)
                return found;
        }
        return null;
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ProtoLink.Communicator.Windows", "MainWindow.xaml")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate Communicator.Windows repo root from " + AppContext.BaseDirectory);
    }
}
