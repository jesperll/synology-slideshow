using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;

namespace SynologySlideshow.Api.Services;

public class ChannelTimerStartup : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ChannelPlaybackService _playback;

    public ChannelTimerStartup(IServiceScopeFactory scopeFactory, ChannelPlaybackService playback)
    {
        _scopeFactory = scopeFactory;
        _playback = playback;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var ids = await db.Channels.Select(c => c.Id).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            _playback.StartTimer(id);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
