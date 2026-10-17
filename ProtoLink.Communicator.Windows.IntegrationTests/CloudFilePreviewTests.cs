using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>AT-C2 pick-source: extension → preview kind; mirror vs remote.</summary>
[Trait("Category", "Offline")]
public class CloudFilePreviewTests
{
    [Theory]
    [InlineData("photo.png", CloudFilePreviewKind.Image)]
    [InlineData("photo.JPG", CloudFilePreviewKind.Image)]
    [InlineData("anim.gif", CloudFilePreviewKind.Image)]
    [InlineData("shot.webp", CloudFilePreviewKind.Image)]
    [InlineData("readme.txt", CloudFilePreviewKind.Text)]
    [InlineData("notes.md", CloudFilePreviewKind.Text)]
    [InlineData("data.json", CloudFilePreviewKind.Text)]
    [InlineData("table.csv", CloudFilePreviewKind.Text)]
    [InlineData("app.log", CloudFilePreviewKind.Text)]
    [InlineData("doc.pdf", CloudFilePreviewKind.Pdf)]
    [InlineData("app.zip", CloudFilePreviewKind.Other)]
    [InlineData("binary.bin", CloudFilePreviewKind.Other)]
    [InlineData("noext", CloudFilePreviewKind.Other)]
    public void KindFromFileName_MapsExtension(string fileName, CloudFilePreviewKind expected)
    {
        Assert.Equal(expected, CloudFilePreview.KindFromFileName(fileName));
    }

    [Fact]
    public void PreferLocalMirror_WhenPathPresent()
    {
        Assert.True(CloudFilePreview.PreferLocalMirror(@"C:\mirror\photo.png"));
        Assert.False(CloudFilePreview.PreferLocalMirror(null));
        Assert.False(CloudFilePreview.PreferLocalMirror(""));
    }

    [Fact]
    public void TryGetExistingMirrorPath_UsesMappedBreadcrumbWhenFileExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-cloud-preview-" + Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(root, "photo.png");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(filePath, [1, 2, 3]);
        try
        {
            var folderId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            var breadcrumb = new[]
            {
                new CloudBreadcrumbEntry { Id = folderId, Name = "Mapped" }
            };
            var mappings = new[]
            {
                new CloudSyncMapping { CloudFolderId = folderId, LocalPath = root, CloudFolderName = "Mapped" }
            };

            var resolved = CloudFilePreview.TryGetExistingMirrorPath("photo.png", breadcrumb, mappings);
            Assert.Equal(filePath, resolved);
            Assert.True(CloudFilePreview.PreferLocalMirror(resolved));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void TryGetExistingMirrorPath_NullWhenMissingOnDisk()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-cloud-preview-miss-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var folderId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
            var breadcrumb = new[]
            {
                new CloudBreadcrumbEntry { Id = folderId, Name = "Mapped" }
            };
            var mappings = new[]
            {
                new CloudSyncMapping { CloudFolderId = folderId, LocalPath = root, CloudFolderName = "Mapped" }
            };

            Assert.Null(CloudFilePreview.TryGetExistingMirrorPath("missing.txt", breadcrumb, mappings));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void TryGetExistingMirrorPath_NullWhenUnmapped()
    {
        var breadcrumb = new[]
        {
            new CloudBreadcrumbEntry { Id = Guid.NewGuid(), Name = "Cloud" }
        };
        Assert.Null(CloudFilePreview.TryGetExistingMirrorPath("readme.txt", breadcrumb, Array.Empty<CloudSyncMapping>()));
    }
}
