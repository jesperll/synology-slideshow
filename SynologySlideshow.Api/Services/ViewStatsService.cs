using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;

namespace SynologySlideshow.Api.Services;

public record SlideViewStat(int SlideId, int ViewCount);

// One bar of the views histogram: how many photos have been shown exactly ViewCount times.
public record ViewCountBucket(int ViewCount, int PhotoCount);

public record ChannelStats(
    int TotalViews,
    SlideViewStat[] TopViewed,
    SlideViewStat[] LeastViewed,
    int NeverViewedCount,
    int PhotoCount,
    ViewCountBucket[] Histogram)
{
    public string? ChannelName { get; init; }
    public int? AlbumId { get; init; }
}

public class ViewStatsService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ViewStatsService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    private const int ListSize = 10;

    // Dense from 0 to the highest count, so empty counts show up as empty bars instead of gaps.
    private static ViewCountBucket[] BuildHistogram(List<SlideViewStat> ranked)
    {
        if (ranked.Count == 0) return Array.Empty<ViewCountBucket>();

        var photosPerCount = ranked.GroupBy(v => v.ViewCount).ToDictionary(g => g.Key, g => g.Count());
        var max = photosPerCount.Keys.Max();
        return Enumerable.Range(0, max + 1)
            .Select(count => new ViewCountBucket(count, photosPerCount.GetValueOrDefault(count)))
            .ToArray();
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

    // Only photos that have been shown have a row, so without the album's full slide list the
    // "least viewed" list could never show a photo with zero views. When albumSlideIds is given,
    // photos that were never shown count as 0 views and the top/least lists only cover that album.
    public async Task<ChannelStats> GetStatsAsync(int channelId, IReadOnlyCollection<int>? albumSlideIds = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();

        var views = await db.SlideViews
            .Where(v => v.ChannelId == channelId)
            .ToListAsync();

        var totalViews = views.Sum(v => v.ViewCount);
        var ranked = views.Select(v => new SlideViewStat(v.SlideId, v.ViewCount)).ToList();
        var neverViewedCount = 0;

        if (albumSlideIds != null)
        {
            var inAlbum = albumSlideIds.ToHashSet();
            ranked = ranked.Where(v => inAlbum.Contains(v.SlideId)).ToList();
            var viewed = ranked.Select(v => v.SlideId).ToHashSet();
            var neverViewed = inAlbum.Where(id => !viewed.Contains(id)).ToList();
            neverViewedCount = neverViewed.Count;
            ranked.AddRange(neverViewed.Select(id => new SlideViewStat(id, 0)));
        }

        var topViewed = ranked.OrderByDescending(v => v.ViewCount).Take(ListSize).ToArray();
        var leastViewed = ranked.OrderBy(v => v.ViewCount).Take(ListSize).ToArray();

        return new ChannelStats(totalViews, topViewed, leastViewed, neverViewedCount, ranked.Count, BuildHistogram(ranked));
    }
}
