using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class CloudFileTypeCueTests
{
    [Theory]
    [InlineData("report.pdf", null, "PDF")]
    [InlineData("photo.PNG", "image/png", "PNG")]
    [InlineData("notes.txt", "text/plain", "TXT")]
    [InlineData("archive.zip", null, "ZIP")]
    [InlineData("noext", "application/pdf", "PDF")]
    [InlineData("noext", "image/jpeg", "Image")]
    [InlineData("noext", "video/mp4", "Video")]
    [InlineData("noext", "audio/mpeg", "Audio")]
    [InlineData("noext", "application/json", "JSON")]
    [InlineData("noext", null, "File")]
    [InlineData("noext", "application/octet-stream", "File")]
    public void ShortLabel_PrefersExtensionThenMime(string? name, string? mime, string expected) =>
        Assert.Equal(expected, CloudFileTypeCue.ShortLabel(mime, name));

    [Theory]
    [InlineData("a.png", null, "🖼")]
    [InlineData("a.bin", "image/webp", "🖼")]
    [InlineData("a.pdf", "application/pdf", "📄")]
    [InlineData("a.txt", null, "📄")]
    public void TypeGlyph_ImageVsFile(string? name, string? mime, string expected) =>
        Assert.Equal(expected, CloudFileTypeCue.TypeGlyph(mime, name));
}
