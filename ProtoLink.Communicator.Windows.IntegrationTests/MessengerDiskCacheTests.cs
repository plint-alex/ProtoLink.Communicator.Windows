using ProtoLink.Communicator.Windows.Services.Messenger;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class MessengerDiskCacheTests : IDisposable
{
    private readonly string _dir;
    private readonly MessengerDiskCache _cache;

    public MessengerDiskCacheTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(),
            "ProtoLinkTests",
            "disposable",
            "messenger-disk-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _cache = new MessengerDiskCache(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Best-effort NFR-TEST-1 cleanup.
        }
    }

    [Fact]
    public void Contacts_SaveAsap_ThenLoad_RoundTrips()
    {
        var owner = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var bag = Guid.NewGuid();
        _cache.SaveContactsAsap(owner, new[]
        {
            new MessengerDiskCache.CachedContact(a, "Alice"),
            new MessengerDiskCache.CachedContact(b, "Bob")
        }, bag);

        var loaded = _cache.TryLoadContacts(owner);
        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, c => c.Id == a && c.Name == "Alice");
        Assert.Contains(loaded, c => c.Id == b && c.Name == "Bob");
        Assert.Equal(bag, _cache.TryLoadUserContactsBagId(owner));
        Assert.Empty(_cache.TryLoadContacts(Guid.NewGuid()));
        Assert.Null(_cache.TryLoadUserContactsBagId(Guid.NewGuid()));
    }

    [Fact]
    public void Contacts_SaveAsap_PreservesBagId_WhenOmitted()
    {
        var owner = Guid.NewGuid();
        var bag = Guid.NewGuid();
        var a = Guid.NewGuid();
        _cache.SaveContactsAsap(owner, new[] { new MessengerDiskCache.CachedContact(a, "A") }, bag);
        _cache.SaveContactsAsap(owner, new[]
        {
            new MessengerDiskCache.CachedContact(a, "A"),
            new MessengerDiskCache.CachedContact(Guid.NewGuid(), "B")
        });
        Assert.Equal(bag, _cache.TryLoadUserContactsBagId(owner));
    }

    [Fact]
    public void Messages_SaveAsap_ThenLoad_PreferOnlineRoundTrip()
    {
        var owner = Guid.NewGuid();
        var partner = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        var att = Guid.NewGuid();
        _cache.SaveMessagesAsap(owner, partner, new[]
        {
            new MessengerDiskCache.CachedMessage(
                msgId,
                "hello",
                "Alice",
                new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc),
                IsFromMe: false,
                IsDateSeparator: false,
                TimeLabel: "10:00",
                DayLabel: "",
                AttachmentIds: new[] { att })
        });

        var loaded = _cache.TryLoadMessages(owner, partner);
        Assert.Single(loaded);
        Assert.Equal(msgId, loaded[0].EntityId);
        Assert.Equal("hello", loaded[0].Text);
        Assert.Equal(att, loaded[0].AttachmentIds[0]);
        Assert.Empty(_cache.TryLoadMessages(owner, Guid.NewGuid()));
    }
}
