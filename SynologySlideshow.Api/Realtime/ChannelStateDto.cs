namespace SynologySlideshow.Api.Realtime;

public class ChannelStateDto
{
    public int ChannelId { get; set; }
    public string Name { get; set; } = null!;
    public int? CurrentAlbumId { get; set; }
    public int? CurrentSlideId { get; set; }
    public bool IsPaused { get; set; }
    public bool IsDefault { get; set; }
}
