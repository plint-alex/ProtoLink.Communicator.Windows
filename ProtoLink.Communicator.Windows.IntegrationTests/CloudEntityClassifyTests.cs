using ProtoLink.Communicator.Windows.Core;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

public class CloudEntityClassifyTests
{
    private static readonly Guid FolderType = CloudEntityCodes.TypeCloudFolderId;
    private static readonly Guid FileType = CloudEntityCodes.TypeCloudFileId;

    [Fact]
    public void CodeName_WithTypeCloudFolder_IsFolder()
    {
        Assert.True(CloudEntityClassify.IsFolder(FolderType, "User_Test_1"));
        Assert.False(CloudEntityClassify.IsFile(FolderType, "User_Test_1"));
    }

    [Fact]
    public void CodeName_WithTypeCloudFile_IsFile()
    {
        Assert.True(CloudEntityClassify.IsFile(FileType, "note.txt"));
        Assert.False(CloudEntityClassify.IsFolder(FileType, "note.txt"));
    }

    [Fact]
    public void LegacyCode_CloudFolder_WithoutType_IsFolder()
    {
        Assert.True(CloudEntityClassify.IsFolder(null, CloudEntityCodes.CloudFolder));
        Assert.True(CloudEntityClassify.IsFolder(null, CloudEntityCodes.CloudRoot));
    }

    [Fact]
    public void LegacyCode_CloudFile_WithoutType_IsFile()
    {
        Assert.True(CloudEntityClassify.IsFile(null, CloudEntityCodes.CloudFile));
        Assert.False(CloudEntityClassify.IsFolder(null, CloudEntityCodes.CloudFile));
    }

    [Fact]
    public void TypeWins_Over_LegacyCodeMismatch()
    {
        // Type says folder even if Code still says CloudFile (should not happen; type wins).
        Assert.True(CloudEntityClassify.IsFolder(FolderType, CloudEntityCodes.CloudFile));
        Assert.True(CloudEntityClassify.IsFile(FileType, CloudEntityCodes.CloudFolder));
    }

    [Fact]
    public void CloudRoot_CodeName_WithRootKindParent_IsCloudRoot()
    {
        Assert.True(CloudEntityClassify.IsCloudRoot("Облако", new[] { CloudEntityCodes.RootKindEntityId }));
        Assert.False(CloudEntityClassify.IsCloudRoot("Облако", parentIds: null));
        Assert.True(CloudEntityClassify.IsCloudRoot(CloudEntityCodes.CloudRoot, parentIds: null));
    }

    [Fact]
    public void Navigable_KnownCloudRoot_WithoutLegacyCode()
    {
        Assert.True(CloudEntityClassify.IsNavigableCloudEntity(null, "Облако", isKnownCloudRoot: true));
        Assert.False(CloudEntityClassify.IsNavigableCloudEntity(null, "Облако", isKnownCloudRoot: false));
        Assert.True(CloudEntityClassify.IsNavigableCloudEntity(FolderType, "MyNotesFolder"));
    }
}
