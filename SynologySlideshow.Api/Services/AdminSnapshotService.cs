using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Realtime;

namespace SynologySlideshow.Api.Services;

public class AdminSnapshotService
{
    private readonly SlideshowDbContext _db;
    private readonly PresenceTracker _presence;
    private readonly ChannelPlaybackService _playback;

    public AdminSnapshotService(SlideshowDbContext db, PresenceTracker presence, ChannelPlaybackService playback)
    {
        _db = db;
        _presence = presence;
        _playback = playback;
    }

    public async Task<AdminSnapshot> BuildAsync()
    {
        // Built from the rows loaded here (no per-channel DB re-query — see the admin-interface
        // plan's final-review fix for why). GetGroupMembers is a pure in-memory lookup, so adding
        // it back here doesn't reintroduce that problem.
        var channels = await _db.Channels.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        var entries = channels
            .Select(channel =>
            {
                var linkedWith = _playback.GetGroupMembers(channel.Id).Where(id => id != channel.Id).ToArray();
                return new AdminChannelEntry(
                    channel.Id,
                    channel.Name,
                    channel.CurrentAlbumId,
                    channel.CurrentSlideId,
                    channel.IsPaused,
                    _presence.GetViewerCount(channel.Id),
                    linkedWith);
            })
            .ToArray();
        return new AdminSnapshot(_presence.GetAnonymousCount(), entries);
    }
}
