using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Services;
using Xunit;

namespace SynologySlideshow.Api.Tests.Services;

public class ViewStatsServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"stats-test-{Guid.NewGuid()}.db");
    private readonly ServiceProvider _provider;
    private readonly ViewStatsService _stats;

    public ViewStatsServiceTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<SlideshowDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        _provider = services.BuildServiceProvider();

        using (var scope = _provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<SlideshowDbContext>().Database.Migrate();
        }

        _stats = new ViewStatsService(_provider.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task FirstViewOfASlideStartsItsCountAtOne()
    {
        await _stats.RecordViewAsync(channelId: 1, slideId: 10);

        var stats = await _stats.GetStatsAsync(1);

        Assert.Equal(1, stats.TotalViews);
        Assert.Contains(stats.TopViewed, s => s.SlideId == 10 && s.ViewCount == 1);
    }

    [Fact]
    public async Task RepeatedViewsOfTheSameSlideAccumulate()
    {
        await _stats.RecordViewAsync(1, 10);
        await _stats.RecordViewAsync(1, 10);
        await _stats.RecordViewAsync(1, 10);

        var stats = await _stats.GetStatsAsync(1);

        Assert.Equal(3, stats.TotalViews);
        Assert.Contains(stats.TopViewed, s => s.SlideId == 10 && s.ViewCount == 3);
    }

    [Fact]
    public async Task StatsAreScopedPerChannel()
    {
        await _stats.RecordViewAsync(1, 10);
        await _stats.RecordViewAsync(2, 10);

        var statsChannel1 = await _stats.GetStatsAsync(1);

        Assert.Equal(1, statsChannel1.TotalViews);
    }

    [Fact]
    public async Task TopAndLeastViewedAreOrderedByCount()
    {
        await _stats.RecordViewAsync(1, 10);
        await _stats.RecordViewAsync(1, 10);
        await _stats.RecordViewAsync(1, 10);
        await _stats.RecordViewAsync(1, 20);

        var stats = await _stats.GetStatsAsync(1);

        Assert.Equal(10, stats.TopViewed.First().SlideId);
        Assert.Equal(20, stats.LeastViewed.First().SlideId);
    }

    [Fact]
    public async Task PhotosThatWereNeverShownCountAsZeroViewsWhenTheAlbumIsKnown()
    {
        await _stats.RecordViewAsync(1, 10);
        await _stats.RecordViewAsync(1, 10);
        await _stats.RecordViewAsync(1, 20);

        var stats = await _stats.GetStatsAsync(1, new[] { 10, 20, 30, 40 });

        Assert.Equal(2, stats.NeverViewedCount);
        Assert.Equal(0, stats.LeastViewed.First().ViewCount);
        Assert.Contains(stats.LeastViewed, s => s.SlideId == 30 && s.ViewCount == 0);
        Assert.Equal(10, stats.TopViewed.First().SlideId);
        Assert.Equal(3, stats.TotalViews);
    }

    [Fact]
    public async Task HistogramCountsHowManyPhotosHaveEachViewCountIncludingNone()
    {
        // photo 10: 3 views, 20: 1 view, 30: 1 view, 40: never shown, 50: never shown
        foreach (var _ in Enumerable.Range(0, 3)) await _stats.RecordViewAsync(1, 10);
        await _stats.RecordViewAsync(1, 20);
        await _stats.RecordViewAsync(1, 30);

        var stats = await _stats.GetStatsAsync(1, new[] { 10, 20, 30, 40, 50 });

        Assert.Equal(5, stats.PhotoCount);
        Assert.Equal(
            new[] { (0, 2), (1, 2), (2, 0), (3, 1) },
            stats.Histogram.Select(b => (b.ViewCount, b.PhotoCount)));
    }

    [Fact]
    public async Task HistogramWithoutAKnownAlbumCoversOnlyPhotosThatWereShown()
    {
        await _stats.RecordViewAsync(1, 10);
        await _stats.RecordViewAsync(1, 10);

        var stats = await _stats.GetStatsAsync(1);

        Assert.Equal(1, stats.PhotoCount);
        Assert.Equal(new[] { (0, 0), (1, 0), (2, 1) }, stats.Histogram.Select(b => (b.ViewCount, b.PhotoCount)));
    }

    [Fact]
    public async Task HistogramIsEmptyWhenThereAreNoPhotos()
    {
        var stats = await _stats.GetStatsAsync(1);

        Assert.Empty(stats.Histogram);
        Assert.Equal(0, stats.PhotoCount);
    }

    [Fact]
    public async Task TopAndLeastViewedListUpToTenPhotos()
    {
        foreach (var slideId in Enumerable.Range(1, 15)) await _stats.RecordViewAsync(1, slideId);

        var stats = await _stats.GetStatsAsync(1);

        Assert.Equal(10, stats.TopViewed.Length);
        Assert.Equal(10, stats.LeastViewed.Length);
    }

    [Fact]
    public async Task RankingsOnlyCoverThePhotosInTheGivenAlbum()
    {
        await _stats.RecordViewAsync(1, 10);
        await _stats.RecordViewAsync(1, 99); // from an album the channel no longer shows
        await _stats.RecordViewAsync(1, 99);

        var stats = await _stats.GetStatsAsync(1, new[] { 10 });

        Assert.DoesNotContain(stats.TopViewed, s => s.SlideId == 99);
        Assert.DoesNotContain(stats.LeastViewed, s => s.SlideId == 99);
        Assert.Equal(3, stats.TotalViews);
        Assert.Equal(0, stats.NeverViewedCount);
    }

    public void Dispose()
    {
        _provider.Dispose();
        // Microsoft.Data.Sqlite pools native connections, which keeps the file
        // handle open on Windows even after the provider (and its DbContexts) is disposed.
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
