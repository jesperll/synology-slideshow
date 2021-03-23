using System.Text.Json.Serialization;

namespace SynologySlideshow.Core.Models;

public class PhotoAdditional : AlbumAdditional
{
    [JsonPropertyName("orientation")] public int Orientation { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; } = null!;
    [JsonPropertyName("resolution")] public PhotoResolution Resolution { get; set; } = null!;
    [JsonPropertyName("person")] public Person[] Person { get; set; } = null!;
    [JsonPropertyName("address")] public Address Address { get; set; } = null!;
    [JsonPropertyName("tag")] public Tag[] Tags { get; set; } = null!;
}

public class Tag
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public required string Name { get; set; }
}