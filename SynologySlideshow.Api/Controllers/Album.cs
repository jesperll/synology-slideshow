namespace SynologySlideshow.Api.Controllers;

public class Album
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Thumbnail { get; set; }
}
