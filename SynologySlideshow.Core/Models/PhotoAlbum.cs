using System.Text.Json.Serialization;
using SynologyNet.Models.Responses.Photo;

namespace SynologySlideshow.Core.Models;

public class PhotoAlbum : Album
{
    [JsonPropertyName("additional")]
    public AlbumAdditional Additional { get; set; } = null!;

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; private set; }

    public PhotoAlbum WithThumbnail(string uri)
    {
        Thumbnail = uri;
        return this;
    }
}