using System.Text.Json.Serialization;

namespace SynologySlideshow.Core.Models;

public class Person
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;
    [JsonPropertyName("item_count")]
    public int ItemCount { get; set; }
    [JsonPropertyName("show")]
    public bool Show { get; set; }
    [JsonPropertyName("additional")]
    public PersonAdditional Additional { get; set; } = null!;
}