namespace ProtoLink.Communicator.Windows.Models;

public class LoginContract
{
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int GiveinPlaceId { get; set; }
}
