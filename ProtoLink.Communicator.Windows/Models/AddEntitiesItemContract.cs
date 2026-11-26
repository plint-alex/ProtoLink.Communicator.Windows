namespace ProtoLink.Communicator.Windows.Models;

/// <summary>
/// One row for <c>POST api/Entities/AddEntities</c>.
/// Optional <see cref="TempId"/> becomes Entity.Id and may appear in later items' ParentIds
/// (parents-before-children).
/// </summary>
public class AddEntitiesItemContract : AddEntityContract
{
    public Guid? TempId { get; set; }
}
