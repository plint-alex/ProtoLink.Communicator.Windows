namespace ProtoLink.Communicator.Windows.Models;

public class GetEntitiesContract
{
    public Guid[]? Ids { get; set; }
    public Guid[]? ParentIds { get; set; }
    public Guid[]? IdsToFindParents { get; set; }
    public int Skip { get; set; }
    public int? Take { get; set; }
    public bool IncludeValues { get; set; }
}
