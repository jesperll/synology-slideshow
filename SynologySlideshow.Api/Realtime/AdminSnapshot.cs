namespace SynologySlideshow.Api.Realtime;

public record AdminChannelEntry(int ChannelId, string Name, int? CurrentAlbumId, int? CurrentSlideId, bool IsPaused, int ViewerCount);

public record AdminSnapshot(int AnonymousCount, AdminChannelEntry[] Channels);
