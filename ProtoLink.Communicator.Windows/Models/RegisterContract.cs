namespace ProtoLink.Communicator.Windows.Models;

public class RegisterContract
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterResult
{
    public bool Success { get; set; }
    public bool UserUpdated { get; set; }
    public string? EmailError { get; set; }
    public string? Error { get; set; }
}
