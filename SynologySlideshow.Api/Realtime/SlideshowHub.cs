using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Services;

namespace SynologySlideshow.Api.Realtime;

public class SlideshowHub : Hub
{
    public const string AdminGroupName = "admin";

    private readonly SlideshowDbContext _db;
    private readonly ChannelPlaybackService _playback;
    private readonly PresenceTracker _presence;
    private readonly AdminSnapshotService _snapshotService;
    private readonly ILogger<SlideshowHub> _logger;

    public SlideshowHub(SlideshowDbContext db, ChannelPlaybackService playback, PresenceTracker presence, AdminSnapshotService snapshotService, ILogger<SlideshowHub> logger)
    {
        _db = db;
        _playback = playback;
        _presence = presence;
        _snapshotService = snapshotService;
        _logger = logger;
    }

    public static string GroupName(int channelId) => $"channel:{channelId}";

    public override async Task OnConnectedAsync()
    {
        _presence.OnConnected();
        await BroadcastPresenceAsync();
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _presence.OnDisconnected(Context.ConnectionId);
        await BroadcastPresenceAsync();
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<AdminSnapshot> JoinAdmin()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroupName);
        _presence.OnJoinedAdmin(Context.ConnectionId);
        return await _snapshotService.BuildAsync();
    }

    public async Task<ChannelStateDto?> JoinChannel(string channelName)
    {
        var normalized = channelName.Trim().ToUpperInvariant();
        var channel = await _db.Channels.SingleOrDefaultAsync(c => c.NormalizedName == normalized);
        if (channel == null) return null;

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(channel.Id));
        _presence.OnJoinedChannel(Context.ConnectionId, channel.Id);
        await BroadcastPresenceAsync();
        return await _playback.GetStateAsync(channel.Id);
    }

    public async Task LeaveChannel(int channelId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(channelId));
        _presence.OnLeftChannel(Context.ConnectionId, channelId);
        await BroadcastPresenceAsync();
    }

    public Task<ChannelStateDto> RequestNextSlide(int channelId) => _playback.AdvanceAsync(channelId, 1);

    public Task<ChannelStateDto> RequestPreviousSlide(int channelId) => _playback.AdvanceAsync(channelId, -1);

    public Task<ChannelStateDto> RequestJumpToSlide(int channelId, int slideId) => _playback.JumpAsync(channelId, slideId);

    public Task<ChannelStateDto> RequestTogglePause(int channelId) => _playback.TogglePauseAsync(channelId);

    public Task<ChannelStateDto> RequestSwitchAlbum(int channelId, int albumId) => _playback.SetAlbumAsync(channelId, albumId);

    // Presence broadcasts are best-effort: a failure here must never abort the caller's
    // connect/disconnect/join/leave handling for an unrelated connection.
    private async Task BroadcastPresenceAsync()
    {
        try
        {
            var snapshot = await _snapshotService.BuildAsync();
            await Clients.Group(AdminGroupName).SendAsync("PresenceChanged", snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast presence update to admins");
        }
    }
}
