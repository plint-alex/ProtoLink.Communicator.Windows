using System.Text.Json.Serialization;

namespace ProtoLink.Communicator.Windows.Models;

public class GetEntitiesResult
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("creationTime")]
    public DateTime CreationTime { get; set; }

    [JsonPropertyName("updateTime")]
    public DateTime UpdateTime { get; set; }

    [JsonPropertyName("values")]
    public List<EntityValue>? Values { get; set; }

    [JsonPropertyName("userWhoCreatedId")]
    public Guid? UserWhoCreatedId { get; set; }

    /// <summary>Populated for Shared with me rows — effective CanWrite from the caller's direct grant(s).</summary>
    [JsonPropertyName("canWrite")]
    public bool? CanWrite { get; set; }
}
