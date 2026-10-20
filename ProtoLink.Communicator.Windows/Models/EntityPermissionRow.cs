using System.Text.Json.Serialization;

namespace ProtoLink.Communicator.Windows.Models;

/// <summary>api/Entities/GetPermissions row (M3 shareability).</summary>
public sealed class EntityPermissionRow
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("permissionForId")]
    public Guid PermissionForId { get; set; }

    [JsonPropertyName("permissionForCode")]
    public string? PermissionForCode { get; set; }

    [JsonPropertyName("canWrite")]
    public bool CanWrite { get; set; }

    [JsonPropertyName("isUser")]
    public bool IsUser { get; set; }

    [JsonPropertyName("principalKind")]
    public string? PrincipalKind { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("inheritedFromEntityId")]
    public Guid? InheritedFromEntityId { get; set; }
}
