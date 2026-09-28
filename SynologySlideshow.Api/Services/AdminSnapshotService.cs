using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Realtime;

namespace SynologySlideshow.Api.Services;

public class AdminSnapshotService
{
    private readonly SlideshowDbContext _db;
    private readonly ChannelPlaybackService _playback;
    private readonly PresenceTracker _presence;

    public AdminSnapshotService(SlideshowDbContext db, ChannelPlaybackService playback, PresenceTracker presence)
    {
        _db = db;
        _playback = playback;
        _presence = presence;
    }

    public async Task<AdminSnapshot> BuildAsync()
    {
        var channels = await _db.Channels.OrderBy(c => c.Name).ToListAsync();
        var entries = new List<AdminChannelEntry>();
        foreach (var channel in channels)
        {
            var state = await _playback.GetStateAsync(channel.Id);
            entries.Add(new AdminChannelEntry(
                channel.Id,
                channel.Name,
                state.CurrentAlbumId,
                state.CurrentSlideId,
                state.IsPaused,
                _presence.GetViewerCount(channel.Id)));
        }
        return new AdminSnapshot(_presence.GetAnonymousCount(), entries.ToArray());
    }
}
