namespace ProtoLink.Communicator.Windows.Models.Share;

public sealed class CreateShareLinkContract
{
    public Guid EntityId { get; set; }
    public string? Label { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool NeverExpires { get; set; }
    public bool IncludeSubtree { get; set; }
    public bool CanWrite { get; set; }
}

public sealed class CreateShareLinkResult
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string TokenPrefix { get; set; } = string.Empty;
    public string? Label { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IncludeSubtree { get; set; }
    public bool CanWrite { get; set; }
    public Guid PrincipalEntityId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public int UseCount { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public sealed class ShareLinkListItemResult
{
    public Guid Id { get; set; }
    public string TokenPrefix { get; set; } = string.Empty;
    public string? Label { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool IncludeSubtree { get; set; }
    public bool CanWrite { get; set; }
    public Guid PrincipalEntityId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string? CreatedByDisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
    public int UseCount { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsLive { get; set; }
}

public sealed class UpdateShareLinkContract
{
    public Guid ShareLinkId { get; set; }
    public string? Label { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool NeverExpires { get; set; }
}

public sealed class RevokeShareLinkContract
{
    public Guid ShareLinkId { get; set; }
}

public sealed class ShareGrantResult
{
    public Guid PermissionId { get; set; }
    public Guid PermissionForId { get; set; }
    public string PrincipalKind { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool CanWrite { get; set; }
    public Guid? ShareLinkId { get; set; }
    public string? TokenPrefix { get; set; }
    public string? Label { get; set; }
}

/// <summary>Grantee Leave Shared with me (UX §7b / AT-S46).</summary>
public sealed class LeaveSharedWithMeContract
{
    public Guid EntityId { get; set; }
}

public sealed class LeaveSharedWithMeResult
{
    public Guid EntityId { get; set; }

    /// <summary>True when a direct personal grant was removed; false when already absent (idempotent).</summary>
    public bool Left { get; set; }
}

/// <summary>Outbound Shared by me root (AT-S51 / AT-S52 / AT-S53). No raw tokens.</summary>
public sealed class SharedByMeRootResult
{
    public Guid EntityId { get; set; }

    public string? EntityName { get; set; }

    public List<SharedByMePrincipalPreviewResult> PeoplePreview { get; set; } = new();

    public int PeopleMoreCount { get; set; }

    public int LiveGuestLinkCount { get; set; }

    public bool HasPublicAccess { get; set; }
}

public sealed class SharedByMePrincipalPreviewResult
{
    public string PrincipalKind { get; set; } = string.Empty;

    public Guid PermissionForId { get; set; }

    public string DisplayName { get; set; } = string.Empty;
}
