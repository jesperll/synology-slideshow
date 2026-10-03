namespace SynologySlideshow.Api.Controllers;

public class ChannelSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsDefault { get; set; }
}

public class CreateChannelRequest
{
    public string Name { get; set; } = null!;
}
