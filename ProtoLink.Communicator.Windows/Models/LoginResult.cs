namespace ProtoLink.Communicator.Windows.Models;

public class LoginResult
{
    public Guid UserId { get; set; }
    public string Login { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public Guid? RefreshToken { get; set; }
    public DateTime ExpirationTime { get; set; }
    public string? Error { get; set; }
}
