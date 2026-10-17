using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Notes;
using ProtoLink.Communicator.Windows.Services.Sync;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>AT-N5: note-id resolver — mappings + SyncItemMeta fixtures; SyncPathUtil normalisation.</summary>
[Trait("Category", "Offline")]
public class NoteRemoteIdResolverTests
{
    private static readonly Guid MappingCloudId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid NoteRemoteId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid OtherRemoteId = Guid.Parse("99999999-8888-7777-6666-555555555555");

    private readonly NoteRemoteIdResolver _resolver = new(new EmptySyncMappingStore());

    [Fact]
    public void SyncedNoteFolder_ReturnsRemoteId()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-note-resolve-" + Guid.NewGuid().ToString("N"));
        var note = Path.Combine(root, "Films");
        Directory.CreateDirectory(note);
        try
        {
            var mappings = new[]
            {
                new SyncMappingInfo
                {
                    Id = MappingCloudId.ToString("N"),
                    CloudFolderId = MappingCloudId,
                    LocalRootPath = root
                }
            };
            var items = new[]
            {
                new SyncItemMeta
                {
                    MappingId = MappingCloudId.ToString("N"),
                    RemoteId = NoteRemoteId,
                    RelativePath = "films",
                    IsFolder = true
                }
            };

            Assert.Equal(NoteRemoteId, _resolver.ResolveFrom(note, mappings, items));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void UnmappedPath_ReturnsNull()
    {
        var mappings = new[]
        {
            new SyncMappingInfo
            {
                Id = MappingCloudId.ToString("N"),
                CloudFolderId = MappingCloudId,
                LocalRootPath = Path.Combine(Path.GetTempPath(), "other-root-" + Guid.NewGuid().ToString("N"))
            }
        };
        var note = Path.Combine(Path.GetTempPath(), "unmapped-" + Guid.NewGuid().ToString("N"), "page");
        Assert.Null(_resolver.ResolveFrom(note, mappings, Array.Empty<SyncItemMeta>()));
    }

    [Fact]
    public void MappedButNotYetInMeta_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-note-nometa-" + Guid.NewGuid().ToString("N"));
        var note = Path.Combine(root, "NewPage");
        Directory.CreateDirectory(note);
        try
        {
            var mappings = new[]
            {
                new SyncMappingInfo
                {
                    Id = MappingCloudId.ToString("N"),
                    CloudFolderId = MappingCloudId,
                    LocalRootPath = root
                }
            };
            Assert.Null(_resolver.ResolveFrom(note, mappings, Array.Empty<SyncItemMeta>()));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void FileMeta_IsIgnored_OnlyFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-note-filemeta-" + Guid.NewGuid().ToString("N"));
        var note = Path.Combine(root, "Films");
        Directory.CreateDirectory(note);
        try
        {
            var mappingId = MappingCloudId.ToString("N");
            var mappings = new[]
            {
                new SyncMappingInfo
                {
                    Id = mappingId,
                    CloudFolderId = MappingCloudId,
                    LocalRootPath = root
                }
            };
            var items = new[]
            {
                new SyncItemMeta
                {
                    MappingId = mappingId,
                    RemoteId = OtherRemoteId,
                    RelativePath = "films",
                    IsFolder = false
                },
                new SyncItemMeta
                {
                    MappingId = mappingId,
                    RemoteId = NoteRemoteId,
                    RelativePath = "films",
                    IsFolder = true
                }
            };

            Assert.Equal(NoteRemoteId, _resolver.ResolveFrom(note, mappings, items));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void BackslashAndMixedCaseMeta_NormalisedViaSyncPathUtil()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-note-sep-" + Guid.NewGuid().ToString("N"));
        var note = Path.Combine(root, "Films", "SubPage");
        Directory.CreateDirectory(note);
        try
        {
            var mappingId = MappingCloudId.ToString("N");
            var mappings = new[]
            {
                new SyncMappingInfo
                {
                    Id = mappingId,
                    CloudFolderId = MappingCloudId,
                    LocalRootPath = root
                }
            };
            var items = new[]
            {
                new SyncItemMeta
                {
                    MappingId = mappingId,
                    RemoteId = NoteRemoteId,
                    RelativePath = @"Films\SubPage",
                    IsFolder = true
                }
            };

            Assert.Equal(NoteRemoteId, _resolver.ResolveFrom(note, mappings, items));
            Assert.Equal(
                SyncPathUtil.Normalize(@"Films\SubPage"),
                SyncPathUtil.Normalize("films/subpage"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void MappingRootFolder_EmptyRelative_ReturnsRemoteId()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-note-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var mappingId = MappingCloudId.ToString("N");
            var mappings = new[]
            {
                new SyncMappingInfo
                {
                    Id = mappingId,
                    CloudFolderId = MappingCloudId,
                    LocalRootPath = root
                }
            };
            var items = new[]
            {
                new SyncItemMeta
                {
                    MappingId = mappingId,
                    RemoteId = NoteRemoteId,
                    RelativePath = "",
                    IsFolder = true
                }
            };

            Assert.Equal(NoteRemoteId, _resolver.ResolveFrom(root, mappings, items));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void NestedMapping_PrefersLongestLocalRoot()
    {
        var outer = Path.Combine(Path.GetTempPath(), "pl-note-nest-" + Guid.NewGuid().ToString("N"));
        var inner = Path.Combine(outer, "Notes");
        var note = Path.Combine(inner, "Page");
        Directory.CreateDirectory(note);
        try
        {
            var outerId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
            var innerId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
            var mappings = new[]
            {
                new SyncMappingInfo
                {
                    Id = outerId.ToString("N"),
                    CloudFolderId = outerId,
                    LocalRootPath = outer
                },
                new SyncMappingInfo
                {
                    Id = innerId.ToString("N"),
                    CloudFolderId = innerId,
                    LocalRootPath = inner
                }
            };
            var items = new[]
            {
                new SyncItemMeta
                {
                    MappingId = outerId.ToString("N"),
                    RemoteId = OtherRemoteId,
                    RelativePath = "notes/page",
                    IsFolder = true
                },
                new SyncItemMeta
                {
                    MappingId = innerId.ToString("N"),
                    RemoteId = NoteRemoteId,
                    RelativePath = "page",
                    IsFolder = true
                }
            };

            Assert.Equal(NoteRemoteId, _resolver.ResolveFrom(note, mappings, items));
        }
        finally
        {
            try { Directory.Delete(outer, recursive: true); } catch { /* temp */ }
        }
    }

    private sealed class EmptySyncMappingStore : ISyncMappingStore
    {
        public IReadOnlyList<Models.CloudSyncMapping> Load() => Array.Empty<Models.CloudSyncMapping>();
        public void Save(IEnumerable<Models.CloudSyncMapping> mappings) { }
    }
}
