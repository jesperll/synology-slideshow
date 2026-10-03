namespace SynologySlideshow.Api.Realtime;

public record AdminChannelEntry(int ChannelId, string Name, int? CurrentAlbumId, int? CurrentSlideId, bool IsPaused, bool IsDefault, int ViewerCount, int[] LinkedChannelIds);

public record AdminSnapshot(AdminChannelEntry[] Channels);
