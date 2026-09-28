using Microsoft.AspNetCore.SignalR;

namespace SynologySlideshow.Api.Realtime;

public class SlideshowHub : Hub
{
    public static string GroupName(int channelId) => $"channel:{channelId}";
}
