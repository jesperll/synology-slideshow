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
    private readonly SyncGroupService _syncGroups;
    private readonly ViewStatsService _viewStats;
    private readonly ConcurrentDictionary<int, Timer> _timers = new();

    public ChannelPlaybackService(
        ISlideSource slideSource,
        IServiceScopeFactory scopeFactory,
        IHubContext<SlideshowHub> hubContext,
        ILogger<ChannelPlaybackService> logger,
        SyncGroupService syncGroups,
        ViewStatsService viewStats)
    {
        _slideSource = slideSource;
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _logger = logger;
        _syncGroups = syncGroups;
        _viewStats = viewStats;
    }

    public async Task<ChannelStateDto> GetStateAsync(int channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        return ToDto(channel);
    }

    public Task<ChannelStateDto> AdvanceAsync(int channelId, int offset) =>
        ApplyToGroupAsync(channelId, id => AdvanceOneAsync(id, offset));

    public Task<ChannelStateDto> JumpAsync(int channelId, int slideId) =>
        ApplyToGroupAsync(channelId, id => JumpOneAsync(id, slideId));

    public async Task<ChannelStateDto> TogglePauseAsync(int channelId)
    {
        var current = await GetStateAsync(channelId);
        var target = !current.IsPaused;
        return await ApplyToGroupAsync(channelId, id => SetPausedOneAsync(id, target));
    }

    public Task<ChannelStateDto> SetAlbumAsync(int channelId, int albumId) =>
        ApplyToGroupAsync(channelId, id => SetAlbumOneAsync(id, albumId));

    public async Task<LinkResult> LinkAsync(IReadOnlyCollection<int> channelIds)
    {
        var distinctIds = channelIds.Distinct().ToList();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var existingCount = await db.Channels.CountAsync(c => distinctIds.Contains(c.Id));
        if (existingCount != distinctIds.Count)
            return new LinkResult(false, "One or more channels don't exist.");

        var (success, error) = _syncGroups.Link(distinctIds);
        if (!success) return new LinkResult(false, error);

        var seedState = await GetStateAsync(distinctIds[0]);
        foreach (var memberId in distinctIds.Skip(1))
        {
            if (seedState.CurrentAlbumId is int albumId)
            {
                await SetAlbumOneAsync(memberId, albumId);
            }
            else
            {
                // "No album" is a real state to mirror: TickAsync assumes every member shares the
                // driving member's album.
                await ClearAlbumOneAsync(memberId);
            }
            if (seedState.CurrentSlideId is int slideId)
            {
                await JumpOneAsync(memberId, slideId);
            }
            else
            {
                // SetAlbumOneAsync lands on the album's first slide; a seed with no current slide
                // would otherwise leave every member permanently one slide ahead of it.
                await ClearSlideOneAsync(memberId);
            }
            var memberState = await GetStateAsync(memberId);
            if (memberState.IsPaused != seedState.IsPaused)
            {
                await SetPausedOneAsync(memberId, seedState.IsPaused);
            }
        }

        return new LinkResult(true, null);
    }

    public Task<ChannelStateDto> UnlinkAsync(int channelId)
    {
        _syncGroups.Unlink(channelId);
        return GetStateAsync(channelId);
    }

    public IReadOnlySet<int> GetGroupMembers(int channelId) => _syncGroups.GetGroupMembers(channelId);

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

        var members = _syncGroups.GetGroupMembers(channelId);
        if (members.Count > 1 && members.Min() != channelId)
        {
            return; // a lower-id member's timer already drives this group's advance
        }

        var slides = GetSlides(channel.CurrentAlbumId);
        if (slides.Length == 0) return;

        foreach (var memberId in members)
        {
            await AdvanceOneAsync(memberId, 1);
        }
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

    private async Task<ChannelStateDto> ApplyToGroupAsync(int originId, Func<int, Task<ChannelStateDto>> applyOne)
    {
        var members = _syncGroups.GetGroupMembers(originId);
        ChannelStateDto? originResult = null;
        foreach (var memberId in members)
        {
            var dto = await applyOne(memberId);
            if (memberId == originId) originResult = dto;
        }
        return originResult!;
    }

    private async Task<ChannelStateDto> AdvanceOneAsync(int channelId, int offset)
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
        if (slides.Length > 0 && dto.CurrentSlideId is int shownSlideId)
        {
            await RecordViewSafeAsync(channelId, shownSlideId);
        }
        ResetTimer(channelId);
        return dto;
    }

    private async Task<ChannelStateDto> JumpOneAsync(int channelId, int slideId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        var slides = GetSlides(channel.CurrentAlbumId);
        var found = Array.FindIndex(slides, s => s.Id == slideId) >= 0;
        if (found)
        {
            channel.CurrentSlideId = slideId;
            await db.SaveChangesAsync();
        }

        var dto = await BuildAndBroadcastAsync(channel);
        if (found && dto.CurrentSlideId is int shownSlideId)
        {
            await RecordViewSafeAsync(channelId, shownSlideId);
        }
        ResetTimer(channelId);
        return dto;
    }

    private async Task<ChannelStateDto> ClearSlideOneAsync(int channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        if (channel.CurrentSlideId != null)
        {
            channel.CurrentSlideId = null;
            await db.SaveChangesAsync();
        }

        var dto = await BuildAndBroadcastAsync(channel);
        ResetTimer(channelId);
        return dto;
    }

    private async Task<ChannelStateDto> ClearAlbumOneAsync(int channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        if (channel.CurrentAlbumId != null || channel.CurrentSlideId != null)
        {
            channel.CurrentAlbumId = null;
            channel.CurrentSlideId = null;
            await db.SaveChangesAsync();
        }

        var dto = await BuildAndBroadcastAsync(channel);
        ResetTimer(channelId);
        return dto;
    }

    private async Task<ChannelStateDto> SetPausedOneAsync(int channelId, bool isPaused)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        channel.IsPaused = isPaused;
        await db.SaveChangesAsync();

        var dto = await BuildAndBroadcastAsync(channel);
        ResetTimer(channelId);
        return dto;
    }

    private async Task<ChannelStateDto> SetAlbumOneAsync(int channelId, int albumId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        var slides = GetSlides(albumId);
        channel.CurrentAlbumId = albumId;
        channel.CurrentSlideId = slides.Length > 0 ? slides[0].Id : null;
        await db.SaveChangesAsync();

        var dto = await BuildAndBroadcastAsync(channel);
        if (dto.CurrentSlideId is int shownSlideId)
        {
            await RecordViewSafeAsync(channelId, shownSlideId);
        }
        ResetTimer(channelId);
        return dto;
    }

    private async Task RecordViewSafeAsync(int channelId, int slideId)
    {
        try
        {
            await _viewStats.RecordViewAsync(channelId, slideId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record a slide view for channel {ChannelId}, slide {SlideId}", channelId, slideId);
        }
    }

    private SlideRef[] GetSlides(int? albumId) =>
        albumId is int id ? _slideSource.GetSlides(id) : Array.Empty<SlideRef>();

    private static int CurrentSlideIndex(Channel channel, SlideRef[] slides) =>
        Array.FindIndex(slides, s => s.Id == channel.CurrentSlideId);

    private async Task<ChannelStateDto> BuildAndBroadcastAsync(Channel channel)
    {
        var dto = ToDto(channel);
        await _hubContext.Clients.Group(SlideshowHub.GroupName(channel.Id)).SendAsync("ChannelStateChanged", dto);
        await _hubContext.Clients.Group(SlideshowHub.AdminGroupName).SendAsync("ChannelStateChanged", dto);
        return dto;
    }

    private static ChannelStateDto ToDto(Channel channel) => new()
    {
        ChannelId = channel.Id,
        Name = channel.Name,
        CurrentAlbumId = channel.CurrentAlbumId,
        CurrentSlideId = channel.CurrentSlideId,
        IsPaused = channel.IsPaused,
        IsDefault = channel.IsDefault
    };
}
