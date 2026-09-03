namespace ProtoLink.Communicator.Windows.Models;

public class RefreshTokenResult
{
    public string AccessToken { get; set; } = string.Empty;
    public Guid? RefreshToken { get; set; }
    public DateTime ExpirationTime { get; set; }
}
