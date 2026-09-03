namespace ProtoLink.Communicator.Windows.Models;

public class TokenData
{
    public string AccessToken { get; set; } = string.Empty;
    public Guid? RefreshToken { get; set; }
    public DateTime ExpirationTime { get; set; }
    public Guid UserId { get; set; }
    public string Login { get; set; } = string.Empty;
}
