using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Sync;
using Xunit;
using Xunit.Abstractions;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Live SyncEngine E2E against PROTOLINK_API_BASE (default http://protolink.ru).
/// Device A mutates FS; Device B + API must converge. Requires PROTOLINK_TEST_PASSWORD.
/// </summary>
public class SyncEngineE2EIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public SyncEngineE2EIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task AddFile_DeviceA_AppearsOnApiAndDeviceB()
    {
        await using var h = await SyncTestHarness.TryCreateAsync();
        if (h == null) { _output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD"); return; }

        h.WriteRelativeFile(h.DeviceARoot, "notes/page/index.html", "<h1>page</h1>\n");
        await h.ReconcileAAsync();

        var pageFolder = await h.FindRemoteByRelativePathAsync("notes/page");
        Assert.NotNull(pageFolder);
        Assert.Equal(CloudEntityCodes.CloudFolder, pageFolder!.Code);

        var files = await h.ListRemoteNamedAsync(pageFolder.Id);
        var cloudFiles = files.Where(f => f.Entity.Code == CloudEntityCodes.CloudFile).ToList();
        Assert.Single(cloudFiles);
        Assert.Equal("index.html", cloudFiles[0].DisplayName, ignoreCase: true);
        Assert.False(CloudApiService.IsSyntheticCloudFileDisplayName(cloudFiles[0].Entity, cloudFiles[0].DisplayName));

        await h.ReconcileBAsync();
        Assert.True(h.LocalExists(h.DeviceBRoot, "notes/page/index.html"));
        Assert.Equal("<h1>page</h1>\n", h.ReadRelativeFile(h.DeviceBRoot, "notes/page/index.html"));
        Assert.Equal(0, await h.CountSyntheticCloudFilesAsync(pageFolder.Id));
    }

    [Fact]
    public async Task ChangeContent_DeviceA_PropagatesToDeviceB_SingleRemoteFile()
    {
        await using var h = await SyncTestHarness.TryCreateAsync();
        if (h == null) { _output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD"); return; }

        h.WriteRelativeFile(h.DeviceARoot, "notes/page/index.html", "v1");
        await h.ReconcileAAsync();
        await h.ReconcileBAsync();

        var pageFolder = await h.FindRemoteByRelativePathAsync("notes/page");
        Assert.NotNull(pageFolder);
        var before = await h.ListRemoteNamedAsync(pageFolder!.Id);
        var fileBefore = before.Single(f => f.Entity.Code == CloudEntityCodes.CloudFile);
        var remoteId = fileBefore.Entity.Id;

        h.WriteRelativeFile(h.DeviceARoot, "notes/page/index.html", "v2-changed");
        await h.ReconcileAAsync();
        await h.ReconcileBAsync();

        Assert.Equal("v2-changed", h.ReadRelativeFile(h.DeviceBRoot, "notes/page/index.html"));
        var after = await h.ListRemoteNamedAsync(pageFolder.Id);
        var cloudFiles = after.Where(f => f.Entity.Code == CloudEntityCodes.CloudFile).ToList();
        Assert.Single(cloudFiles);
        Assert.Equal(remoteId, cloudFiles[0].Entity.Id);
        Assert.Equal("index.html", cloudFiles[0].DisplayName, ignoreCase: true);
        Assert.Equal(0, await h.CountSyntheticCloudFilesAsync(pageFolder.Id));
    }

    [Fact]
    public async Task ChangeContent_SameSize_DeviceA_PropagatesToDeviceB()
    {
        await using var h = await SyncTestHarness.TryCreateAsync();
        if (h == null) { _output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD"); return; }

        h.WriteRelativeFile(h.DeviceARoot, "notes/page/index.html", "v1!!");
        await h.ReconcileAAsync();
        await h.ReconcileBAsync();
        Assert.Equal("v1!!", h.ReadRelativeFile(h.DeviceBRoot, "notes/page/index.html"));

        // Same length (4), different content — must still sync (Films same-size edits).
        h.WriteRelativeFile(h.DeviceARoot, "notes/page/index.html", "v2!!");
        await h.ReconcileAAsync();
        await h.ReconcileBAsync();

        Assert.Equal("v2!!", h.ReadRelativeFile(h.DeviceBRoot, "notes/page/index.html"));
    }

    [Fact]
    public async Task RenameFile_DeviceA_PropagatesToDeviceBAndApi()
    {
        await using var h = await SyncTestHarness.TryCreateAsync();
        if (h == null) { _output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD"); return; }

        h.WriteRelativeFile(h.DeviceARoot, "notes/page/index.html", "body");
        await h.ReconcileAAsync();
        await h.ReconcileBAsync();

        var from = Path.Combine(h.DeviceARoot, "notes", "page", "index.html");
        var to = Path.Combine(h.DeviceARoot, "notes", "page", "readme.html");
        File.Move(from, to);

        await h.ReconcileAAsync();
        await h.ReconcileBAsync();

        Assert.False(h.LocalExists(h.DeviceBRoot, "notes/page/index.html"));
        Assert.True(h.LocalExists(h.DeviceBRoot, "notes/page/readme.html"));
        Assert.Equal("body", h.ReadRelativeFile(h.DeviceBRoot, "notes/page/readme.html"));

        var pageFolder = await h.FindRemoteByRelativePathAsync("notes/page");
        Assert.NotNull(pageFolder);
        var files = (await h.ListRemoteNamedAsync(pageFolder!.Id))
            .Where(f => f.Entity.Code == CloudEntityCodes.CloudFile).ToList();
        Assert.Single(files);
        Assert.Equal("readme.html", files[0].DisplayName, ignoreCase: true);
        Assert.Equal(0, await h.CountSyntheticCloudFilesAsync(pageFolder.Id));
    }

    [Fact]
    public async Task MoveFile_DeviceA_PropagatesToDeviceBAndApi()
    {
        await using var h = await SyncTestHarness.TryCreateAsync();
        if (h == null) { _output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD"); return; }

        h.WriteRelativeFile(h.DeviceARoot, "notes/page/index.html", "moved-content");
        await h.ReconcileAAsync();
        await h.ReconcileBAsync();

        var destDir = Path.Combine(h.DeviceARoot, "notes", "other");
        Directory.CreateDirectory(destDir);
        File.Move(
            Path.Combine(h.DeviceARoot, "notes", "page", "index.html"),
            Path.Combine(destDir, "readme.html"));

        await h.ReconcileAAsync();
        await h.ReconcileBAsync();

        Assert.False(h.LocalExists(h.DeviceBRoot, "notes/page/index.html"));
        Assert.True(h.LocalExists(h.DeviceBRoot, "notes/other/readme.html"));
        Assert.Equal("moved-content", h.ReadRelativeFile(h.DeviceBRoot, "notes/other/readme.html"));

        var oldParent = await h.FindRemoteByRelativePathAsync("notes/page");
        Assert.NotNull(oldParent);
        Assert.Equal(0, await h.CountCloudFilesAsync(oldParent!.Id));

        var newParent = await h.FindRemoteByRelativePathAsync("notes/other");
        Assert.NotNull(newParent);
        var files = (await h.ListRemoteNamedAsync(newParent!.Id))
            .Where(f => f.Entity.Code == CloudEntityCodes.CloudFile).ToList();
        Assert.Single(files);
        Assert.Equal("readme.html", files[0].DisplayName, ignoreCase: true);
    }

    [Fact]
    public async Task RenameFolder_DeviceA_PropagatesToDeviceBAndApi()
    {
        await using var h = await SyncTestHarness.TryCreateAsync();
        if (h == null) { _output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD"); return; }

        h.WriteRelativeFile(h.DeviceARoot, "notes/page/index.html", "in-folder");
        await h.ReconcileAAsync();
        await h.ReconcileBAsync();

        Directory.Move(
            Path.Combine(h.DeviceARoot, "notes", "page"),
            Path.Combine(h.DeviceARoot, "notes", "renamed"));

        await h.ReconcileAAsync();
        await h.ReconcileBAsync();

        Assert.False(h.LocalExists(h.DeviceBRoot, "notes/page"));
        Assert.True(h.LocalExists(h.DeviceBRoot, "notes/renamed/index.html"));
        Assert.Equal("in-folder", h.ReadRelativeFile(h.DeviceBRoot, "notes/renamed/index.html"));

        Assert.Null(await h.FindRemoteByRelativePathAsync("notes/page"));
        var renamed = await h.FindRemoteByRelativePathAsync("notes/renamed");
        Assert.NotNull(renamed);
        Assert.Equal(CloudEntityCodes.CloudFolder, renamed!.Code);
        var files = (await h.ListRemoteNamedAsync(renamed.Id))
            .Where(f => f.Entity.Code == CloudEntityCodes.CloudFile).ToList();
        Assert.Single(files);
        Assert.Equal("index.html", files[0].DisplayName, ignoreCase: true);
    }

    [Fact]
    public async Task AddThenDelete_DeviceA_RemovesFromApiAndDeviceB()
    {
        await using var h = await SyncTestHarness.TryCreateAsync();
        if (h == null) { _output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD"); return; }

        h.WriteRelativeFile(h.DeviceARoot, "notes/page/index.html", "temp");
        await h.ReconcileAAsync();
        await h.ReconcileBAsync();
        Assert.True(h.LocalExists(h.DeviceBRoot, "notes/page/index.html"));

        File.Delete(Path.Combine(h.DeviceARoot, "notes", "page", "index.html"));
        await h.ReconcileAAsync();
        await h.ReconcileBAsync();

        Assert.False(h.LocalExists(h.DeviceBRoot, "notes/page/index.html"));
        var pageFolder = await h.FindRemoteByRelativePathAsync("notes/page");
        Assert.NotNull(pageFolder);
        Assert.Equal(0, await h.CountCloudFilesAsync(pageFolder!.Id));
    }

    [Fact]
    public async Task IdempotentResync_DoesNotCreateTrash()
    {
        await using var h = await SyncTestHarness.TryCreateAsync();
        if (h == null) { _output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD"); return; }

        h.WriteRelativeFile(h.DeviceARoot, "notes/page/index.html", "<h1>stable</h1>");
        await h.ReconcileAAsync();

        var pageFolder = await h.FindRemoteByRelativePathAsync("notes/page");
        Assert.NotNull(pageFolder);
        var count1 = await h.CountCloudFilesAsync(pageFolder!.Id);
        var synth1 = await h.CountSyntheticCloudFilesAsync(pageFolder.Id);

        await h.ReconcileAAsync();
        await h.ReconcileAAsync();

        var count2 = await h.CountCloudFilesAsync(pageFolder.Id);
        var synth2 = await h.CountSyntheticCloudFilesAsync(pageFolder.Id);
        _output.WriteLine($"CloudFiles before={count1} after={count2}; synthetic before={synth1} after={synth2}");

        Assert.Equal(count1, count2);
        Assert.Equal(synth1, synth2);
        Assert.Equal(1, count2);
        Assert.Equal(0, synth2);
    }

    [Fact]
    public async Task TrashProbe_NamelessRemotePlusLocalFile_DocumentsSyntheticGrowth()
    {
        await using var h = await SyncTestHarness.TryCreateAsync();
        if (h == null) { _output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD"); return; }

        // Seed a notes page folder remotely (named), then create a CloudFile + AddFile the way sync does.
        var notesId = await h.Api.AddEntityAsync(CloudEntityCodes.CloudFolder, new[] { h.CloudFolderId }, "notes");
        var pageId = await h.Api.AddEntityAsync(CloudEntityCodes.CloudFolder, new[] { notesId }, "page");
        var fileId = await h.Api.AddEntityAsync(CloudEntityCodes.CloudFile, new[] { pageId }, "index.html");
        await using (var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<h1>seed</h1>")))
            await h.Api.AddFileAsync(fileId, "index.html", ms, "text/html");

        var afterUpload = await h.ListRemoteNamedAsync(pageId);
        var cloudFiles = afterUpload.Where(f => f.Entity.Code == CloudEntityCodes.CloudFile).ToList();
        var syntheticAfterUpload = cloudFiles.Count(f =>
            CloudApiService.IsSyntheticCloudFileDisplayName(f.Entity, f.DisplayName));
        _output.WriteLine(
            $"After AddFile: cloudFiles={cloudFiles.Count}, synthetic={syntheticAfterUpload}, names=[{string.Join(", ", cloudFiles.Select(c => c.DisplayName))}]");

        // Local file at same path — empty metadata on A so bootstrap may skip nameless remotes,
        // then local phase may re-upload if remote is invisible.
        h.WriteRelativeFile(h.DeviceARoot, "notes/page/index.html", "<h1>local</h1>");
        await h.ReconcileAAsync();
        await h.ReconcileAAsync();

        var afterSync = await h.ListRemoteNamedAsync(pageId);
        var filesAfter = afterSync.Where(f => f.Entity.Code == CloudEntityCodes.CloudFile).ToList();
        var synthAfter = filesAfter.Count(f =>
            CloudApiService.IsSyntheticCloudFileDisplayName(f.Entity, f.DisplayName));
        _output.WriteLine(
            $"After reconcile×2: cloudFiles={filesAfter.Count}, synthetic={synthAfter}, names=[{string.Join(", ", filesAfter.Select(c => c.DisplayName))}]");

        // Document the bug path: if AddFile dropped the name, sync can accumulate duplicates.
        if (syntheticAfterUpload > 0)
        {
            Assert.True(
                filesAfter.Count >= 1,
                "Expected at least one cloud file after local sync against nameless remote.");
            _output.WriteLine(
                "BUG PATH CONFIRMED: AddFile produced synthetic name; post-sync file count=" + filesAfter.Count);
        }
        else
        {
            // Server preserves name — engine should keep a single named file.
            Assert.Single(filesAfter);
            Assert.Equal("index.html", filesAfter[0].DisplayName, ignoreCase: true);
            Assert.Equal(0, synthAfter);
        }
    }

    [Fact]
    public async Task EmptyLocalStub_Heals_WhenRemoteHasNoSize()
    {
        await using var h = await SyncTestHarness.TryCreateAsync();
        if (h == null) { _output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD"); return; }

        // Listing never sets SizeBytes; also disable Content-Length probe.
        h.UseNoRemoteSizeProbe();

        const string relative = "films/index.html";
        const string content = "<p>Movie notes</p>";
        h.WriteRelativeFile(h.DeviceARoot, relative, content);
        await h.ReconcileAAsync();
        await h.ReconcileBAsync();
        Assert.Equal(content, h.ReadRelativeFile(h.DeviceBRoot, relative));

        // Corrupt Device B to empty stub + meta size 0 (Films bug).
        h.WriteRelativeFile(h.DeviceBRoot, relative, "");
        var meta = h.StoreB.GetAll(h.MappingId)
            .Single(m => !m.IsFolder && m.RelativePath == SyncPathUtil.Normalize(relative));
        meta.SizeBytes = 0;
        h.StoreB.Upsert(meta);

        await h.ReconcileBAsync();

        Assert.Equal(content, h.ReadRelativeFile(h.DeviceBRoot, relative));
        var healed = h.StoreB.GetAll(h.MappingId)
            .Single(m => !m.IsFolder && m.RelativePath == SyncPathUtil.Normalize(relative));
        Assert.True(healed.SizeBytes > 0, "Meta size should reflect healed content");
        Assert.Equal(healed.SizeBytes, new FileInfo(Path.Combine(h.DeviceBRoot, "films", "index.html")).Length);
    }

    [Fact]
    public async Task LocalEdit_Writes_WhenRemoteHasNoSize()
    {
        await using var h = await SyncTestHarness.TryCreateAsync();
        if (h == null) { _output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD"); return; }

        h.UseNoRemoteSizeProbe();

        h.WriteRelativeFile(h.DeviceARoot, "notes/page/index.html", "v1");
        await h.ReconcileAAsync();
        await h.ReconcileBAsync();

        h.WriteRelativeFile(h.DeviceARoot, "notes/page/index.html", "v2-no-remote-size");
        await h.ReconcileAAsync();
        await h.ReconcileBAsync();

        Assert.Equal("v2-no-remote-size", h.ReadRelativeFile(h.DeviceBRoot, "notes/page/index.html"));
    }
}
