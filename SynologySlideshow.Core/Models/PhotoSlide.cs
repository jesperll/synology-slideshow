using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SynologyNet.Models.Responses.Photo;

namespace SynologySlideshow.Core.Models;

public class PhotoSlide : Photo
{
    [JsonPropertyName("additional")]
    public PhotoAdditional Additional { get; set; } = null!;

    public PhotoSlide Extend(string uri, string? location, JsonNode debugInfo)
    {
        Uri = uri;
        Location = location.NullIfEmpty();
        DebugInfo = debugInfo;
        return this;
    }

    [JsonPropertyName("uri")]
    public string Uri { get; private set; } = null!;

    [JsonPropertyName("date")]
    public DateTime Date => DateTimeOffset.FromUnixTimeSeconds(Time).DateTime;

    [JsonPropertyName("description")] 
    public string? Description => Additional.Description.NullIfEmpty();
    [JsonPropertyName("location")]
    public string? Location { get; set; }
    [JsonIgnore]
    public JsonNode? DebugInfo { get; set; }
}