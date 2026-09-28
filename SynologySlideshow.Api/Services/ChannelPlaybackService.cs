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

internal class ChannelRuntimeState
{
    public int? CurrentSlideIndex;
}

public class ChannelPlaybackService
{
    private static readonly TimeSpan AdvanceInterval = TimeSpan.FromSeconds(30);

    private readonly ISlideSource _slideSource;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<SlideshowHub> _hubContext;
    private readonly ILogger<ChannelPlaybackService> _logger;
    private readonly ConcurrentDictionary<int, ChannelRuntimeState> _runtime = new();
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

        return ToDto(channel, GetOrCreateRuntime(channelId));
    }

    public async Task<ChannelStateDto> AdvanceAsync(int channelId, int offset)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        var runtime = GetOrCreateRuntime(channelId);
        var slides = GetSlides(channel.CurrentAlbumId);
        if (slides.Length > 0)
        {
            var current = runtime.CurrentSlideIndex ?? -1;
            runtime.CurrentSlideIndex = ((current + offset) % slides.Length + slides.Length) % slides.Length;
        }

        return await BuildAndBroadcastAsync(channel, runtime);
    }

    public async Task<ChannelStateDto> JumpAsync(int channelId, int slideId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        var runtime = GetOrCreateRuntime(channelId);
        var slides = GetSlides(channel.CurrentAlbumId);
        var index = Array.FindIndex(slides, s => s.Id == slideId);
        if (index >= 0)
        {
            runtime.CurrentSlideIndex = index;
        }

        return await BuildAndBroadcastAsync(channel, runtime);
    }

    public async Task<ChannelStateDto> TogglePauseAsync(int channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        channel.IsPaused = !channel.IsPaused;
        await db.SaveChangesAsync();

        return await BuildAndBroadcastAsync(channel, GetOrCreateRuntime(channelId));
    }

    public async Task<ChannelStateDto> SetAlbumAsync(int channelId, int albumId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        channel.CurrentAlbumId = albumId;
        await db.SaveChangesAsync();

        var runtime = GetOrCreateRuntime(channelId);
        runtime.CurrentSlideIndex = null;

        return await BuildAndBroadcastAsync(channel, runtime);
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

        var runtime = GetOrCreateRuntime(channelId);
        var slides = GetSlides(channel.CurrentAlbumId);
        if (slides.Length == 0) return;

        var current = runtime.CurrentSlideIndex ?? -1;
        runtime.CurrentSlideIndex = (current + 1 + slides.Length) % slides.Length;

        await BuildAndBroadcastAsync(channel, runtime);
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
        _runtime.TryRemove(channelId, out _);
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

    private ChannelRuntimeState GetOrCreateRuntime(int channelId) =>
        _runtime.GetOrAdd(channelId, _ => new ChannelRuntimeState());

    private SlideRef[] GetSlides(int? albumId) =>
        albumId is int id ? _slideSource.GetSlides(id) : Array.Empty<SlideRef>();

    private async Task<ChannelStateDto> BuildAndBroadcastAsync(Channel channel, ChannelRuntimeState runtime)
    {
        var dto = ToDto(channel, runtime);
        await _hubContext.Clients.Group(SlideshowHub.GroupName(channel.Id)).SendAsync("ChannelStateChanged", dto);
        return dto;
    }

    private ChannelStateDto ToDto(Channel channel, ChannelRuntimeState runtime)
    {
        var slides = GetSlides(channel.CurrentAlbumId);
        int? currentSlideId = runtime.CurrentSlideIndex is int index && index >= 0 && index < slides.Length
            ? slides[index].Id
            : null;

        return new ChannelStateDto
        {
            ChannelId = channel.Id,
            Name = channel.Name,
            CurrentAlbumId = channel.CurrentAlbumId,
            CurrentSlideId = currentSlideId,
            IsPaused = channel.IsPaused
        };
    }
}
