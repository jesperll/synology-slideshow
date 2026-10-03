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

    public void Dispose()
    {
        _provider.Dispose();
        // Microsoft.Data.Sqlite pools native connections, which keeps the file
        // handle open on Windows even after the provider (and its DbContexts) is disposed.
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
