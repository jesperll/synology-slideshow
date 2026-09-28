using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Realtime;

namespace SynologySlideshow.Api.Services;

public class ChannelNotFoundException : Exception
{
    public ChannelNotFoundException(int channelId) : base($"Channel {channelId} was not found.")
    {
    }
}

public class ChannelPlaybackService
{
    private static readonly TimeSpan AdvanceInterval = TimeSpan.FromSeconds(30);

    private readonly ISlideSource _slideSource;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<SlideshowHub> _hubContext;
    private readonly ILogger<ChannelPlaybackService> _logger;
    private readonly ConcurrentDictionary<int, Timer> _timers = new();

    public ChannelPlaybackService(ISlideSource slideSource, IServiceScopeFactory scopeFactory, IHubContext<SlideshowHub> hubContext, ILogger<ChannelPlaybackService> logger)
    {
        _slideSource = slideSource;
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task<ChannelStateDto> GetStateAsync(int channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        return ToDto(channel);
    }

    public async Task<ChannelStateDto> AdvanceAsync(int channelId, int offset)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        var slides = GetSlides(channel.CurrentAlbumId);
        if (slides.Length > 0)
        {
            var current = CurrentSlideIndex(channel, slides);
            var next = ((current + offset) % slides.Length + slides.Length) % slides.Length;
            channel.CurrentSlideId = slides[next].Id;
            await db.SaveChangesAsync();
        }

        var dto = await BuildAndBroadcastAsync(channel);
        ResetTimer(channelId);
        return dto;
    }

    public async Task<ChannelStateDto> JumpAsync(int channelId, int slideId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        var slides = GetSlides(channel.CurrentAlbumId);
        if (Array.FindIndex(slides, s => s.Id == slideId) >= 0)
        {
            channel.CurrentSlideId = slideId;
            await db.SaveChangesAsync();
        }

        var dto = await BuildAndBroadcastAsync(channel);
        ResetTimer(channelId);
        return dto;
    }

    public async Task<ChannelStateDto> TogglePauseAsync(int channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        channel.IsPaused = !channel.IsPaused;
        await db.SaveChangesAsync();

        var dto = await BuildAndBroadcastAsync(channel);
        ResetTimer(channelId);
        return dto;
    }

    public async Task<ChannelStateDto> SetAlbumAsync(int channelId, int albumId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        var slides = GetSlides(albumId);
        channel.CurrentAlbumId = albumId;
        channel.CurrentSlideId = slides.Length > 0 ? slides[0].Id : null;
        await db.SaveChangesAsync();

        var dto = await BuildAndBroadcastAsync(channel);
        ResetTimer(channelId);
        return dto;
    }

    public async Task TickAsync(int channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId);
        if (channel == null)
        {
            StopTimer(channelId);
            return;
        }
        if (channel.IsPaused) return;

        var slides = GetSlides(channel.CurrentAlbumId);
        if (slides.Length == 0) return;

        var current = CurrentSlideIndex(channel, slides);
        channel.CurrentSlideId = slides[(current + 1 + slides.Length) % slides.Length].Id;
        await db.SaveChangesAsync();

        await BuildAndBroadcastAsync(channel);
    }

    public void StartTimer(int channelId)
    {
        _timers.GetOrAdd(channelId, id => new Timer(OnTick, id, AdvanceInterval, AdvanceInterval));
    }

    public void StopTimer(int channelId)
    {
        if (_timers.TryRemove(channelId, out var timer))
        {
            timer.Dispose();
        }
    }

    public void ResetTimer(int channelId)
    {
        if (_timers.TryGetValue(channelId, out var timer))
        {
            timer.Change(AdvanceInterval, AdvanceInterval);
        }
    }

    private void OnTick(object? state) => _ = OnTickAsync((int)state!);

    private async Task OnTickAsync(int channelId)
    {
        try
        {
            await TickAsync(channelId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to tick playback for channel {ChannelId}", channelId);
        }
    }

    private SlideRef[] GetSlides(int? albumId) =>
        albumId is int id ? _slideSource.GetSlides(id) : Array.Empty<SlideRef>();

    // -1 when there is no current slide (or it's no longer in the album), so the next advance lands on the first slide.
    private static int CurrentSlideIndex(Channel channel, SlideRef[] slides) =>
        Array.FindIndex(slides, s => s.Id == channel.CurrentSlideId);

    private async Task<ChannelStateDto> BuildAndBroadcastAsync(Channel channel)
    {
        var dto = ToDto(channel);
        await _hubContext.Clients.Group(SlideshowHub.GroupName(channel.Id)).SendAsync("ChannelStateChanged", dto);
        return dto;
    }

    private static ChannelStateDto ToDto(Channel channel) => new()
    {
        ChannelId = channel.Id,
        Name = channel.Name,
        CurrentAlbumId = channel.CurrentAlbumId,
        CurrentSlideId = channel.CurrentSlideId,
        IsPaused = channel.IsPaused
    };
}
