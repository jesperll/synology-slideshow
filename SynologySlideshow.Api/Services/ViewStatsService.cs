using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;

namespace SynologySlideshow.Api.Services;

public record SlideViewStat(int SlideId, int ViewCount);

public record ChannelStats(int TotalViews, SlideViewStat[] TopViewed, SlideViewStat[] LeastViewed);

public class ViewStatsService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ViewStatsService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RecordViewAsync(int channelId, int slideId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();

        var existing = await db.SlideViews.SingleOrDefaultAsync(v => v.ChannelId == channelId && v.SlideId == slideId);
        if (existing == null)
        {
            db.SlideViews.Add(new SlideView { ChannelId = channelId, SlideId = slideId, ViewCount = 1 });
        }
        else
        {
            existing.ViewCount++;
        }

        await db.SaveChangesAsync();
    }

    public async Task<ChannelStats> GetStatsAsync(int channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();

        var views = await db.SlideViews
            .Where(v => v.ChannelId == channelId)
            .ToListAsync();

        var totalViews = views.Sum(v => v.ViewCount);
        var topViewed = views.OrderByDescending(v => v.ViewCount).Take(5)
            .Select(v => new SlideViewStat(v.SlideId, v.ViewCount)).ToArray();
        var leastViewed = views.OrderBy(v => v.ViewCount).Take(5)
            .Select(v => new SlideViewStat(v.SlideId, v.ViewCount)).ToArray();

        return new ChannelStats(totalViews, topViewed, leastViewed);
    }
}
