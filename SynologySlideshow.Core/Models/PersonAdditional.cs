using System.Text.Json.Serialization;

namespace SynologySlideshow.Core.Models;

public class PersonAdditional
{
    [JsonPropertyName("thumbnail")]
    public PhotoThumbnail Thumbnail { get; set; } = null!;
}