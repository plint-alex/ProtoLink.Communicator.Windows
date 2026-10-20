using System.Text.Json.Serialization;

namespace ProtoLink.Communicator.Windows.Models.Share;

/// <summary>Minimal GetEntity payload for paste-ID name preview (share-sheet-ux §4).</summary>
public sealed class GetEntityCardResult
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }
}
