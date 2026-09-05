using ProtoLink.Communicator.Windows.Services.Sync;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

public class SyncDirectionDecideTests
{
    [Fact]
    public void MissingLocal_Reads()
    {
        Assert.Equal(
            SyncDirectionDecide.Action.Read,
            SyncDirectionDecide.Decide(null, 10L, 0L, DateTime.UtcNow, null));
    }

    [Fact]
    public void EmptyLocal_NoRemoteSize_Reads()
    {
        Assert.Equal(
            SyncDirectionDecide.Action.Read,
            SyncDirectionDecide.Decide(0L, remoteSize: null, metaSize: 0L, DateTime.UtcNow, DateTime.UnixEpoch));
    }

    [Fact]
    public void EmptyLocal_RemoteHasBytes_Reads()
    {
        Assert.Equal(
            SyncDirectionDecide.Action.Read,
            SyncDirectionDecide.Decide(0L, 42L, 0L, DateTime.UtcNow, DateTime.UnixEpoch));
    }

    [Fact]
    public void TinyEditorStub_RemoteLarger_Reads()
    {
        Assert.Equal(
            SyncDirectionDecide.Action.Read,
            SyncDirectionDecide.Decide(11L, 158L, 11L, DateTime.UnixEpoch, DateTime.UnixEpoch));
    }

    [Fact]
    public void LocalGrew_NoRemoteSize_Writes()
    {
        Assert.Equal(
            SyncDirectionDecide.Action.Write,
            SyncDirectionDecide.Decide(10L, remoteSize: null, metaSize: 5L, DateTime.UnixEpoch, DateTime.UnixEpoch));
    }

    [Fact]
    public void RemoteNewerTime_NoRemoteSize_Reads()
    {
        var older = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(
            SyncDirectionDecide.Action.Read,
            SyncDirectionDecide.Decide(5L, remoteSize: null, metaSize: 5L, newer, older));
    }

    [Fact]
    public void Unchanged_NoRemoteSize_Skips()
    {
        Assert.Equal(
            SyncDirectionDecide.Action.Skip,
            SyncDirectionDecide.Decide(5L, remoteSize: null, metaSize: 5L, DateTime.UnixEpoch, DateTime.UnixEpoch));
    }

    [Fact]
    public void SameSize_LocalContentChanged_Writes()
    {
        Assert.Equal(
            SyncDirectionDecide.Action.Write,
            SyncDirectionDecide.Decide(
                localSize: 4L,
                remoteSize: 4L,
                metaSize: 4L,
                remoteTime: DateTime.UnixEpoch,
                metaTime: DateTime.UnixEpoch,
                localContentChanged: true));
    }

    [Fact]
    public void DecideByHash_NoRemoteHash_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ContentHashUtil.DecideByHash("aaaa", remoteHash: "", metaHash: null));
    }

    [Fact]
    public void DecideByHash_Equal_Skips()
    {
        var h = ContentHashUtil.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("same"));
        Assert.Equal(SyncDirectionDecide.Action.Skip, ContentHashUtil.DecideByHash(h, h, h));
    }

    [Fact]
    public void DecideByHash_LocalDiffers_RemoteMatchesMeta_Writes()
    {
        var local = ContentHashUtil.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("bbbb"));
        var remote = ContentHashUtil.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("aaaa"));
        Assert.Equal(SyncDirectionDecide.Action.Write, ContentHashUtil.DecideByHash(local, remote, remote));
    }

    [Fact]
    public void DecideByHash_RemoteDiffersLocalMatchesMeta_Reads()
    {
        var local = ContentHashUtil.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("aaaa"));
        var remote = ContentHashUtil.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("bbbb"));
        Assert.Equal(SyncDirectionDecide.Action.Read, ContentHashUtil.DecideByHash(local, remote, local));
    }

    [Fact]
    public void DecideByHash_EmptyMeta_Diverged_Conflicts()
    {
        var local = ContentHashUtil.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("Test6"));
        var remote = ContentHashUtil.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("Test5"));
        Assert.Equal(SyncDirectionDecide.Action.Conflict, ContentHashUtil.DecideByHash(local, remote, metaHash: null));
        Assert.Equal(
            SyncDirectionDecide.Action.Conflict,
            ContentHashUtil.DecideByHash(local, remote, metaHash: "",
                remoteUpdateTime: DateTime.UnixEpoch,
                metaUpdateTime: DateTime.UnixEpoch));
        Assert.Equal(
            SyncDirectionDecide.Action.Conflict,
            ContentHashUtil.DecideByHash(local, remote, metaHash: "",
                remoteUpdateTime: new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                metaUpdateTime: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void DecideByHash_UnseededMeta_LocalDiffersFromRemote_Conflicts()
    {
        Assert.Equal(
            SyncDirectionDecide.Action.Conflict,
            ContentHashUtil.DecideByHash("aaa", remoteHash: "bbb", metaHash: ""));
        Assert.Equal(
            SyncDirectionDecide.Action.Skip,
            ContentHashUtil.DecideByHash("aaa", remoteHash: "aaa", metaHash: ""));
    }

    [Fact]
    public void DecideByHash_BothDivergedFromMeta_Conflicts()
    {
        var local = ContentHashUtil.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("local"));
        var remote = ContentHashUtil.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("remote"));
        var meta = ContentHashUtil.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("baseline"));
        Assert.Equal(SyncDirectionDecide.Action.Conflict, ContentHashUtil.DecideByHash(local, remote, meta));
    }
}
