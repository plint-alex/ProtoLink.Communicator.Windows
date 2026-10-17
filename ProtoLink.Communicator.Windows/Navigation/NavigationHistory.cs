namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>A place the user left: what was open, where the list was scrolled, and which row was selected.</summary>
public sealed record NavigationEntry<TKey>(TKey Key, double ListScrollOffset = 0, TKey? SelectedKey = default);

/// <summary>
/// Back stack shared by Cloud, Notes and Messenger. Back = history (not parent); every open,
/// including breadcrumb navigation, pushes the entry being left.
/// </summary>
public sealed class NavigationHistory<TKey>
{
    public const int DefaultCapacity = 50;

    private readonly LinkedList<NavigationEntry<TKey>> _entries = new();

    public NavigationHistory(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    public int Capacity { get; }
    public int Count => _entries.Count;
    public bool CanGoBack => _entries.Count > 0;

    public event Action? Changed;

    public void Push(NavigationEntry<TKey> entry)
    {
        _entries.AddLast(entry);
        if (_entries.Count > Capacity)
            _entries.RemoveFirst();
        Changed?.Invoke();
    }

    public bool TryPop(out NavigationEntry<TKey> entry)
    {
        if (_entries.Last is not { } last)
        {
            entry = null!;
            return false;
        }
        _entries.RemoveLast();
        entry = last.Value;
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        if (_entries.Count == 0) return;
        _entries.Clear();
        Changed?.Invoke();
    }
}
