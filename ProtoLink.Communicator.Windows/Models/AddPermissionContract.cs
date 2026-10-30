namespace ProtoLink.Communicator.Windows.Models;

public class AddPermissionContract
{
    public Guid Id { get; set; }
    public Guid PermissionForId { get; set; }
    public bool CanWrite { get; set; }

    /// <summary>
    /// When true, Api D1-stamps inherited grants onto MainParentId descendants (folder Share Add people).
    /// Default false keeps messenger / file root grants root-only.
    /// </summary>
    public bool IncludeSubtree { get; set; }
}
