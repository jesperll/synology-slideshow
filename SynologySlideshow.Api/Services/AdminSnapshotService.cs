using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Realtime;

namespace SynologySlideshow.Api.Services;

public class AdminSnapshotService
{
    private readonly SlideshowDbContext _db;
    private readonly PresenceTracker _presence;

    public AdminSnapshotService(SlideshowDbContext db, PresenceTracker presence)
    {
        _db = db;
        _presence = presence;
    }

    public async Task<AdminSnapshot> BuildAsync()
    {
        // Build every entry from the rows loaded here rather than re-querying each channel's
        // state: one query instead of N+1, and no way to throw for a channel deleted between
        // the list query and a per-channel lookup.
        var channels = await _db.Channels.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        var entries = channels
            .Select(channel => new AdminChannelEntry(
                channel.Id,
                channel.Name,
                channel.CurrentAlbumId,
                channel.CurrentSlideId,
                channel.IsPaused,
                _presence.GetViewerCount(channel.Id)))
            .ToArray();
        return new AdminSnapshot(_presence.GetAnonymousCount(), entries);
    }
}
