namespace ProtoLink.Communicator.Windows.Models;

public class AddPermissionContract
{
    public Guid Id { get; set; }
    public Guid PermissionForId { get; set; }
    public bool CanWrite { get; set; }
}
