namespace ProtoLink.Communicator.Windows.Models;

public class RefreshTokenContract
{
    public string AccessToken { get; set; } = string.Empty;
    public Guid RefreshToken { get; set; }
}
