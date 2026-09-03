namespace ProtoLink.Communicator.Windows.Core;

public static class CloudEntityCodes
{
    public const string CloudRoot = "CloudRoot";
    public const string CloudFolder = "CloudFolder";
    public const string CloudFile = "CloudFile";
    public static readonly Guid NameTypeId = new("00010003-0000-0000-0000-000000000000");
    public static readonly Guid MimeTypeId = new("00010012-0000-0000-0000-000000000000");
}
