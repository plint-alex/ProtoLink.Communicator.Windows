namespace ProtoLink.Communicator.Windows.Services;

/// <summary>API rejected the session (401 / legacy 403). Caller should prompt login, not show stack traces.</summary>
public sealed class CloudAuthRequiredException : Exception
{
    public CloudAuthRequiredException() : base("Please log in.") { }
}
