using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;

namespace SynologySlideshow.Api.Services;

// Ensures the permanent, non-deletable "Default" channel exists. Channel advance timers are
// no longer started here: SlideshowHub starts a channel's timer as soon as its first viewer
// joins (see SyncTimersForGroup), since a channel with nobody watching has no timer to start
// at boot anyway.
public class DefaultChannelSeeder : IHostedService
{
    public const string DefaultChannelName = "Default";

    private readonly IServiceScopeFactory _scopeFactory;

    public DefaultChannelSeeder(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();

        if (!await db.Channels.AnyAsync(c => c.IsDefault, cancellationToken))
        {
            db.Channels.Add(new Channel
            {
                Name = DefaultChannelName,
                NormalizedName = DefaultChannelName.ToUpperInvariant(),
                IsDefault = true
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
