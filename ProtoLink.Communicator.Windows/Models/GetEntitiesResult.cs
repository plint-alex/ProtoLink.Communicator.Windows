using System.Text.Json.Serialization;

namespace ProtoLink.Communicator.Windows.Models;

public class GetEntitiesResult
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("values")]
    public List<EntityValue>? Values { get; set; }
}
