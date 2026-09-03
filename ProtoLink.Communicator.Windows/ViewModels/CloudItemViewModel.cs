namespace ProtoLink.Communicator.Windows.ViewModels;

public class CloudItemViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public bool IsSynced { get; set; }
}
