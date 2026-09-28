using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Services;

namespace SynologySlideshow.Api.Realtime;

public class SlideshowHub : Hub
{
    private readonly SlideshowDbContext _db;
    private readonly ChannelPlaybackService _playback;

    public SlideshowHub(SlideshowDbContext db, ChannelPlaybackService playback)
    {
        _db = db;
        _playback = playback;
    }

    public static string GroupName(int channelId) => $"channel:{channelId}";

    public async Task<ChannelStateDto?> JoinChannel(string channelName)
    {
        var normalized = channelName.Trim().ToUpperInvariant();
        var channel = await _db.Channels.SingleOrDefaultAsync(c => c.NormalizedName == normalized);
        if (channel == null) return null;

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(channel.Id));
        return await _playback.GetStateAsync(channel.Id);
    }

    public Task LeaveChannel(int channelId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(channelId));

    public Task<ChannelStateDto> RequestNextSlide(int channelId) => _playback.AdvanceAsync(channelId, 1);

    public Task<ChannelStateDto> RequestPreviousSlide(int channelId) => _playback.AdvanceAsync(channelId, -1);

    public Task<ChannelStateDto> RequestJumpToSlide(int channelId, int slideId) => _playback.JumpAsync(channelId, slideId);

    public Task<ChannelStateDto> RequestTogglePause(int channelId) => _playback.TogglePauseAsync(channelId);

    public Task<ChannelStateDto> RequestSwitchAlbum(int channelId, int albumId) => _playback.SetAlbumAsync(channelId, albumId);
}
