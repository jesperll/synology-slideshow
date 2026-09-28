namespace SynologySlideshow.Api.Controllers;

public class ChannelSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
}

public class CreateChannelRequest
{
    public string Name { get; set; } = null!;
}
