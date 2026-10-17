using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>AT-N6: shared back stack used by Cloud, Notes and Messenger.</summary>
[Trait("Category", "Offline")]
public class NavigationHistoryTests
{
    [Fact]
    public void Empty_CannotGoBack()
    {
        var history = new NavigationHistory<string>();

        Assert.False(history.CanGoBack);
        Assert.False(history.TryPop(out _));
    }

    [Fact]
    public void Pop_ReturnsEntriesInReverseOrder_WithScrollAndSelection()
    {
        var history = new NavigationHistory<string>();
        history.Push(new NavigationEntry<string>("root", 10, "A"));
        history.Push(new NavigationEntry<string>("A", 20, "B"));

        Assert.True(history.TryPop(out var first));
        Assert.Equal(new NavigationEntry<string>("A", 20, "B"), first);
        Assert.True(history.TryPop(out var second));
        Assert.Equal("root", second.Key);
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void BackIsHistoryNotParent_BreadcrumbNavigationPushes()
    {
        // AT-C3: root → A → A/B → breadcrumb "A" → Back lands on A/B, not root.
        var history = new NavigationHistory<string>();
        history.Push(new("root"));
        history.Push(new("A"));
        history.Push(new("A/B"));

        Assert.True(history.TryPop(out var back));
        Assert.Equal("A/B", back.Key);
    }

    [Fact]
    public void Push_BeyondCapacity_DropsOldest()
    {
        var history = new NavigationHistory<int>();
        for (var i = 0; i < NavigationHistory<int>.DefaultCapacity + 5; i++)
            history.Push(new(i));

        Assert.Equal(NavigationHistory<int>.DefaultCapacity, history.Count);
        var popped = new List<int>();
        while (history.TryPop(out var e))
            popped.Add(e.Key);
        Assert.Equal(54, popped[0]);
        Assert.Equal(5, popped[^1]);
    }

    [Fact]
    public void Changed_RaisedOnPushPopAndClear()
    {
        var history = new NavigationHistory<string>();
        var raised = 0;
        history.Changed += () => raised++;

        history.Push(new("a"));
        history.TryPop(out _);
        history.TryPop(out _);
        history.Push(new("b"));
        history.Clear();
        history.Clear();

        Assert.Equal(4, raised);
    }
}
