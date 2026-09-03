namespace ProtoLink.Communicator.Windows.Models;

public class AddValueContract
{
    public TypeOfValue Type { get; set; }
    public object? Value { get; set; }
    public Guid[] ParentIds { get; set; } = Array.Empty<Guid>();
}
