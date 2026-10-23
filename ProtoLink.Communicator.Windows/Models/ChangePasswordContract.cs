namespace ProtoLink.Communicator.Windows.Models;

public class ChangePasswordContract
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class ChangePasswordResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
