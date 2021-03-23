using System.Text.Json.Serialization;

namespace SynologySlideshow.Core.Models;

public class PhotoThumbnail
{
    [JsonPropertyName("cache_key")]
    public string CacheKey { get; set; } = null!;
    [JsonPropertyName("m")]
    public string? M { get; set; }
    [JsonPropertyName("preview")]
    public string? Preview { get; set; }
    [JsonPropertyName("sm")]
    public string? Sm { get; set; }
    [JsonPropertyName("unit_id")]
    public int UnitId { get; set; }
    [JsonPropertyName("xl")]
    public string? Xl { get; set; }
}