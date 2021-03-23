using System.Text.Json.Serialization;

namespace SynologySlideshow.Core.Models;

public class PhotoResolution
{
    [JsonPropertyName("height")]
    public int Height { get; set; }
    [JsonPropertyName("width ")]
    public int Width { get; set; }
}