using System.Text.Json.Serialization;

namespace ProtoLink.Communicator.Windows.Models;

public class EntityValue
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public object? Value { get; set; }

    [JsonPropertyName("parents")]
    public IEnumerable<Guid> Parents { get; set; } = new List<Guid>();
}
