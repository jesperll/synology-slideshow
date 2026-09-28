namespace SynologySlideshow.Api.Data;

public class Channel
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string NormalizedName { get; set; } = null!;
    public int? CurrentAlbumId { get; set; }
    public int? CurrentSlideId { get; set; }
    public bool IsPaused { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
