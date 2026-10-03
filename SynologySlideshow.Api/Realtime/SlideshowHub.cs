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
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var leftChannelId = _presence.OnDisconnected(Context.ConnectionId);
        if (leftChannelId is int id) SyncTimersForGroup(id);
        await BroadcastPresenceAsync();
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<AdminSnapshot> JoinAdmin()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroupName);
        return await _snapshotService.BuildAsync();
    }

    public async Task<ChannelStateDto?> JoinChannel(string channelName)
    {
        var normalized = channelName.Trim().ToUpperInvariant();
        var channel = await _db.Channels.SingleOrDefaultAsync(c => c.NormalizedName == normalized);
        if (channel == null) return null;

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(channel.Id));
        var previousChannelId = _presence.OnJoinedChannel(Context.ConnectionId, channel.Id);
        SyncTimersForGroup(channel.Id);
        if (previousChannelId is int previousId) SyncTimersForGroup(previousId);
        await BroadcastPresenceAsync();
        return await _playback.GetStateAsync(channel.Id);
    }

    public async Task LeaveChannel(int channelId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(channelId));
        _presence.OnLeftChannel(Context.ConnectionId, channelId);
        SyncTimersForGroup(channelId);
        await BroadcastPresenceAsync();
    }

    public Task<ChannelStateDto> RequestNextSlide(int channelId) => _playback.AdvanceAsync(channelId, 1);

    public Task<ChannelStateDto> RequestPreviousSlide(int channelId) => _playback.AdvanceAsync(channelId, -1);

    public Task<ChannelStateDto> RequestJumpToSlide(int channelId, int slideId) => _playback.JumpAsync(channelId, slideId);

    public Task<ChannelStateDto> RequestTogglePause(int channelId) => _playback.TogglePauseAsync(channelId);

    public Task<ChannelStateDto> RequestSwitchAlbum(int channelId, int albumId) => _playback.SetAlbumAsync(channelId, albumId);

    public async Task<LinkResult> RequestLinkChannels(int[] channelIds)
    {
        var result = await _playback.LinkAsync(channelIds);
        if (result.Success)
        {
            // Linking can give a previously-idle (0-viewer) member a group that now has
            // viewers via its new groupmates, or vice versa for an unlinked member - re-sync
            // every affected channel's timer rather than just the ones passed in.
            foreach (var id in channelIds)
            {
                SyncTimersForGroup(id);
            }
            await BroadcastPresenceAsync();
        }
        return result;
    }

    public async Task<ChannelStateDto> RequestUnlinkChannel(int channelId)
    {
        var state = await _playback.UnlinkAsync(channelId);
        SyncTimersForGroup(channelId);
        await BroadcastPresenceAsync();
        return state;
    }

    // A channel's advance timer should only run while someone is actually watching - either
    // this channel directly, or (for a sync-linked group) any of its groupmates, since a
    // linked group's lowest-id member drives the whole group's advance regardless of which
    // member's viewers keep it alive. Running the timer with nobody watching wastes work and
    // inflates view stats for slides nobody saw; this does not touch the admin-facing
    // IsPaused flag, so an admin's manual pause/play and manual Next/Previous keep working
    // regardless of viewer count.
    private void SyncTimersForGroup(int channelId)
    {
        var members = _playback.GetGroupMembers(channelId);
        var totalViewers = members.Sum(_presence.GetViewerCount);
        foreach (var id in members)
        {
            if (totalViewers == 0) _playback.StopTimer(id);
            else _playback.StartTimer(id);
        }
    }

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
