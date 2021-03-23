using System.Text.Json.Serialization;

namespace SynologySlideshow.Core.Models;

public class PersonThumbnail
{
    [JsonPropertyName("cache_key")]
    public string CacheKey { get; set; } = null!;
}