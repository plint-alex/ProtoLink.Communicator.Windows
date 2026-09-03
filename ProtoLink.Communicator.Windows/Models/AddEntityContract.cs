namespace ProtoLink.Communicator.Windows.Models;

public class AddEntityContract
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool CodeIsUnique { get; set; }
    public int Order { get; set; }
    public Guid[] ParentIds { get; set; } = Array.Empty<Guid>();
    public bool Hidden { get; set; }
    public List<AddValueContract>? Values { get; set; }
    public List<AddPermissionContract>? Permissions { get; set; }
}
