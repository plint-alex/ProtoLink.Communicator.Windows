namespace ProtoLink.Communicator.Windows.Models;

public sealed class ServerVersionResponse
{
    public ServerApiVersionInfo? Api { get; set; }
}

public sealed class ServerApiVersionInfo
{
    public string? Version { get; set; }
    public string? BuildDate { get; set; }
    public string? Framework { get; set; }
}
