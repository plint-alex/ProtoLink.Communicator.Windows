using ProtoLink.Communicator.Windows.Navigation;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class CloudItemSizeCueTests
{
    [Theory]
    [InlineData(null, "—")]
    [InlineData(-1L, "—")]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1048576L, "1 MB")]
    public void Format_KnownAndUnknown(long? bytes, string expected)
    {
        Assert.Equal(expected, CloudItemSizeCue.Format(bytes));
    }

    [Fact]
    public void TryReadLocalFileLength_Folder_ReturnsNull()
    {
        Assert.Null(CloudItemSizeCue.TryReadLocalFileLength(@"C:\any\path", isFolder: true));
    }

    [Fact]
    public void TryReadLocalFileLength_Missing_ReturnsNull()
    {
        Assert.Null(CloudItemSizeCue.TryReadLocalFileLength(
            Path.Combine(Path.GetTempPath(), "protolink-missing-" + Guid.NewGuid().ToString("N") + ".bin"),
            isFolder: false));
    }

    [Fact]
    public void TryReadLocalFileLength_ExistingFile_ReturnsLength()
    {
        var path = Path.Combine(Path.GetTempPath(), "protolink-size-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });
            Assert.Equal(5, CloudItemSizeCue.TryReadLocalFileLength(path, isFolder: false));
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }
}
