namespace SynologySlideshow.Api.Controllers;

public class Slide
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? Uri { get; set; }
}
