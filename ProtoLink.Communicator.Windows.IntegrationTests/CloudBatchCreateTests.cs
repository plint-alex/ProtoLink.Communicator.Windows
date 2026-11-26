using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Services;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Offline: AddEntities batch contract helpers (Sync ApplyLocal parent+children).</summary>
[Trait("Category", "Offline")]
public class CloudBatchCreateTests
{
    [Fact]
    public void ShouldUseBatch_TwoOrMore_True_Single_False()
    {
        Assert.False(CloudBatchCreate.ShouldUseBatch(0));
        Assert.False(CloudBatchCreate.ShouldUseBatch(1));
        Assert.True(CloudBatchCreate.ShouldUseBatch(2));
        Assert.True(CloudBatchCreate.ShouldUseBatch(5));
    }

    [Fact]
    public void ToContracts_SetsTempIdCodeParentsAndNameValue()
    {
        var folder = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var file = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var parent = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

        var contracts = CloudBatchCreate.ToContracts(
        [
            new CloudBatchCreate.PlannedItem(folder, CloudEntityCodes.CloudFolder, [parent], "notes"),
            new CloudBatchCreate.PlannedItem(file, CloudEntityCodes.CloudFile, [folder], "index.html")
        ]);

        Assert.Equal(2, contracts.Count);
        Assert.Equal(folder, contracts[0].TempId);
        Assert.Equal(CloudEntityCodes.CloudFolder, contracts[0].Code);
        Assert.Equal(new[] { parent }, contracts[0].ParentIds);
        Assert.NotNull(contracts[0].Values);
        Assert.Equal("notes", contracts[0].Values![0].Value);
        Assert.Equal(new[] { CloudEntityCodes.NameTypeId }, contracts[0].Values[0].ParentIds);

        Assert.Equal(file, contracts[1].TempId);
        Assert.Equal(CloudEntityCodes.CloudFile, contracts[1].Code);
        Assert.Equal(new[] { folder }, contracts[1].ParentIds);
        Assert.Equal("index.html", contracts[1].Values![0].Value);
    }

    [Fact]
    public void ParentsBeforeChildren_ValidAndForwardRef()
    {
        var folder = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var file = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var root = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

        Assert.True(CloudBatchCreate.ParentsBeforeChildren(
        [
            new(folder, CloudEntityCodes.CloudFolder, [root], "a"),
            new(file, CloudEntityCodes.CloudFile, [folder], "b")
        ]));

        Assert.False(CloudBatchCreate.ParentsBeforeChildren(
        [
            new(file, CloudEntityCodes.CloudFile, [folder], "b"),
            new(folder, CloudEntityCodes.CloudFolder, [root], "a")
        ]));
    }

    [Fact]
    public void ParseIdsResponse_ReadsIdsInOrder()
    {
        var id1 = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var id2 = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var ids = CloudBatchCreate.ParseIdsResponse(
            $$"""{"ids":["{{id1:D}}","{{id2:D}}"]}""");
        Assert.Equal(new[] { id1, id2 }, ids);
    }

    [Fact]
    public void ParseIdsResponse_MissingIds_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CloudBatchCreate.ParseIdsResponse("""{"id":"..."}"""));
    }
}
