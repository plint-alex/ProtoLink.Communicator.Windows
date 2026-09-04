using ProtoLink.Communicator.Windows.Services.Sync;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

public class SyncClassifierTests
{
    [Fact]
    public void LocalRename_UniqueSize_Detected()
    {
        var store = new List<SyncItemMeta>
        {
            new() { MappingId = "m", RemoteId = Guid.NewGuid(), ParentRemoteId = Guid.NewGuid(), RelativePath = "old.txt", IsFolder = false, SizeBytes = 5 }
        };
        var fs = new List<FsEntry> { new() { RelativePath = "new.txt", IsFolder = false, SizeBytes = 5 } };
        var changes = new LocalChangeClassifier().Classify(fs, store);
        Assert.Single(changes.OfType<LocalChange.Renamed>());
        Assert.Empty(changes.OfType<LocalChange.Added>());
        Assert.Empty(changes.OfType<LocalChange.Removed>());
    }

    [Fact]
    public void LocalRename_AmbiguousSize_FallsThrough()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var store = new List<SyncItemMeta>
        {
            new() { MappingId = "m", RemoteId = id1, ParentRemoteId = parent, RelativePath = "a.txt", SizeBytes = 5 },
            new() { MappingId = "m", RemoteId = id2, ParentRemoteId = parent, RelativePath = "b.txt", SizeBytes = 5 }
        };
        var fs = new List<FsEntry>
        {
            new() { RelativePath = "c.txt", SizeBytes = 5 },
            new() { RelativePath = "d.txt", SizeBytes = 5 }
        };
        var changes = new LocalChangeClassifier().Classify(fs, store);
        Assert.Empty(changes.OfType<LocalChange.Renamed>());
        Assert.Equal(2, changes.OfType<LocalChange.Added>().Count());
        Assert.Equal(2, changes.OfType<LocalChange.Removed>().Count());
    }

    [Fact]
    public void LocalAddRemoveUpdate()
    {
        var parent = Guid.NewGuid();
        var store = new List<SyncItemMeta>
        {
            new() { MappingId = "m", RemoteId = Guid.NewGuid(), ParentRemoteId = parent, RelativePath = "keep.txt", SizeBytes = 3 },
            new() { MappingId = "m", RemoteId = Guid.NewGuid(), ParentRemoteId = parent, RelativePath = "gone.txt", SizeBytes = 1 },
            new() { MappingId = "m", RemoteId = Guid.NewGuid(), ParentRemoteId = parent, RelativePath = "chg.txt", SizeBytes = 2 }
        };
        var fs = new List<FsEntry>
        {
            new() { RelativePath = "keep.txt", SizeBytes = 3 },
            new() { RelativePath = "new.txt", SizeBytes = 4 },
            new() { RelativePath = "chg.txt", SizeBytes = 9 }
        };
        var changes = new LocalChangeClassifier().Classify(fs, store);
        Assert.Single(changes.OfType<LocalChange.Added>());
        Assert.Single(changes.OfType<LocalChange.Removed>());
        Assert.Single(changes.OfType<LocalChange.Updated>());
    }

    [Fact]
    public void RemoteRemoved_WhenNotOnServer()
    {
        var meta = new SyncItemMeta
        {
            MappingId = "m",
            RemoteId = Guid.NewGuid(),
            ParentRemoteId = Guid.NewGuid(),
            RelativePath = "x.txt",
            SizeBytes = 1
        };
        var changes = new RemoteChangeClassifier().Classify(
            new[] { meta },
            Array.Empty<RemoteLocated>(),
            new HashSet<Guid> { meta.RemoteId },
            new HashSet<string>());
        Assert.Single(changes.OfType<RemoteChange.Removed>());
    }
}
