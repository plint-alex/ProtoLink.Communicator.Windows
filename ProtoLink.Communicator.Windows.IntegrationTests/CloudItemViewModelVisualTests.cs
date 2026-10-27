using ProtoLink.Communicator.Windows.ViewModels;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class CloudItemViewModelVisualTests
{
    [Fact]
    public void Subtitle_FolderNotSynced()
    {
        var item = new CloudItemViewModel { Name = "Docs", IsFolder = true, IsSynced = false };
        Assert.Equal("Folder", item.SubtitleText);
        Assert.Equal("📁", item.TypeGlyph);
        Assert.Equal(string.Empty, item.SyncBadgeText);
    }

    [Fact]
    public void Subtitle_FolderSynced_IncludesSynced()
    {
        var item = new CloudItemViewModel { Name = "Docs", IsFolder = true, IsSynced = true };
        Assert.Equal("Folder · Synced", item.SubtitleText);
        Assert.Equal("✓", item.SyncBadgeText);
    }

    [Fact]
    public void Subtitle_File_UsesExtensionCue()
    {
        var item = new CloudItemViewModel { Name = "a.txt", IsFolder = false };
        Assert.Equal("TXT", item.SubtitleText);
        Assert.Equal("📄", item.TypeGlyph);
    }

    [Fact]
    public void Subtitle_File_UsesMimeWhenNoExtension()
    {
        var item = new CloudItemViewModel
        {
            Name = "file-abc",
            IsFolder = false,
            MimeType = "image/png"
        };
        Assert.Equal("Image", item.SubtitleText);
        Assert.Equal("🖼", item.TypeGlyph);
    }

    [Fact]
    public void Subtitle_FileSynced_KeepsTypeCue()
    {
        var item = new CloudItemViewModel
        {
            Name = "doc.pdf",
            IsFolder = false,
            IsSynced = true,
            MimeType = "application/pdf"
        };
        Assert.Equal("PDF · Synced", item.SubtitleText);
    }

    [Fact]
    public void Details_TypeAndSize_FolderEmDash()
    {
        var item = new CloudItemViewModel { Name = "Docs", IsFolder = true, SizeBytes = 99 };
        Assert.Equal("Folder", item.TypeDisplay);
        Assert.Equal("—", item.SizeDisplay);
    }

    [Fact]
    public void Details_TypeAndSize_FileCheapBytes()
    {
        var item = new CloudItemViewModel
        {
            Name = "a.txt",
            IsFolder = false,
            SizeBytes = 2048
        };
        Assert.Equal("TXT", item.TypeDisplay);
        Assert.Equal("2 KB", item.SizeDisplay);
    }

    [Fact]
    public void Details_TypeIgnoresSharedCaptionOverride()
    {
        var item = new CloudItemViewModel
        {
            Name = "SharedRoot",
            IsFolder = true,
            SubtitleOverride = "from Alice · can edit"
        };
        Assert.Equal("Folder", item.TypeDisplay);
        Assert.Equal("from Alice · can edit", item.SubtitleText);
    }
}
