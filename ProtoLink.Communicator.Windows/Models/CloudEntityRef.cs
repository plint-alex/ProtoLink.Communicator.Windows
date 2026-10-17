namespace ProtoLink.Communicator.Windows.Models;

public enum CloudEntityKind { Folder, File }

/// <summary>The entity open in the Cloud tab. <see cref="ParentId"/> is null only for the Cloud root.</summary>
public sealed record CloudEntityRef(Guid Id, CloudEntityKind Kind, string Name, Guid? ParentId)
{
    public bool IsFolder => Kind == CloudEntityKind.Folder;
}
