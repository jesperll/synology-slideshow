using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Services;
using SynologySlideshow.Api.Tests.TestDoubles;
using Xunit;

namespace SynologySlideshow.Api.Tests.Services;

public class ChannelPlaybackServiceStatsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"playback-stats-test-{Guid.NewGuid()}.db");
    private readonly ServiceProvider _provider;
    private readonly FakeHubContext _hub = new();
    private readonly FakeSlideSource _slides = new();
    private readonly ChannelPlaybackService _playback;
    private readonly ViewStatsService _viewStats;
    private readonly int _channelId;

    public ChannelPlaybackServiceStatsTests()
    {
        var services = new ServiceCollection();
        // Pooling is off because other test classes call the process-wide SqliteConnection.ClearAllPools()
        // in parallel, which can dispose a pooled handle this class is still using.
        services.AddDbContext<SlideshowDbContext>(o => o.UseSqlite($"Data Source={_dbPath};Pooling=False"));
        _provider = services.BuildServiceProvider();

        using (var scope = _provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<SlideshowDbContext>().Database.Migrate();
        }

        _viewStats = new ViewStatsService(_provider.GetRequiredService<IServiceScopeFactory>());
        _playback = new ChannelPlaybackService(
            _slides,
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _hub,
            new FakeLogger<ChannelPlaybackService>(),
            new SyncGroupService(),
            _viewStats);

        _slides.SlidesByAlbum[1] = new[] { new SlideRef(10), new SlideRef(20) };

        using var setupScope = _provider.CreateScope();
        var db = setupScope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = new Channel { Name = "kitchen", NormalizedName = "KITCHEN", CurrentAlbumId = 1, IsPaused = false };
        db.Channels.Add(channel);
        db.SaveChanges();
        _channelId = channel.Id;
    }

    [Fact]
    public async Task AdvancingRecordsAViewForTheResolvedSlide()
    {
        await _playback.AdvanceAsync(_channelId, 1); // -> slide 10

        var stats = await _viewStats.GetStatsAsync(_channelId);

        Assert.Equal(1, stats.TotalViews);
        Assert.Contains(stats.TopViewed, s => s.SlideId == 10 && s.ViewCount == 1);
    }

    [Fact]
    public async Task JumpingRecordsAViewForTheTargetSlide()
    {
        await _playback.JumpAsync(_channelId, 20);

        var stats = await _viewStats.GetStatsAsync(_channelId);

        Assert.Equal(1, stats.TotalViews);
        Assert.Contains(stats.TopViewed, s => s.SlideId == 20 && s.ViewCount == 1);
    }

    [Fact]
    public async Task RevisitingTheSameSlideAccumulatesItsCount()
    {
        await _playback.AdvanceAsync(_channelId, 1); // -> 10
        await _playback.AdvanceAsync(_channelId, 1); // -> 20
        await _playback.AdvanceAsync(_channelId, 1); // -> 10 again

        var stats = await _viewStats.GetStatsAsync(_channelId);

        Assert.Equal(3, stats.TotalViews);
        Assert.Contains(stats.TopViewed, s => s.SlideId == 10 && s.ViewCount == 2);
    }

    [Fact]
    public async Task SwitchingAlbumsRecordsAViewForTheFirstSlide()
    {
        await _playback.SetAlbumAsync(_channelId, 1);

        var stats = await _viewStats.GetStatsAsync(_channelId);

        Assert.Equal(1, stats.TotalViews);
        Assert.Contains(stats.TopViewed, s => s.SlideId == 10 && s.ViewCount == 1);
    }

    [Fact]
    public async Task TheAutomaticAdvanceTimerRecordsAViewJustLikeAManualRequest()
    {
        await _playback.TickAsync(_channelId); // simulates the 30-second timer firing on its own

        var stats = await _viewStats.GetStatsAsync(_channelId);

        Assert.Equal(1, stats.TotalViews);
        Assert.Contains(stats.TopViewed, s => s.SlideId == 10 && s.ViewCount == 1);
    }

    [Fact]
    public async Task JumpingToAnUnknownSlideRecordsNoView()
    {
        await _playback.JumpAsync(_channelId, 20); // jump to a valid slide

        var statsAfterValidJump = await _viewStats.GetStatsAsync(_channelId);
        Assert.Equal(1, statsAfterValidJump.TotalViews);

        await _playback.JumpAsync(_channelId, 12345); // jump to an unknown slide

        var statsAfterInvalidJump = await _viewStats.GetStatsAsync(_channelId);

        // The view count should still be 1, not 2 (invalid jump should not record a view)
        Assert.Equal(1, statsAfterInvalidJump.TotalViews);
        Assert.Contains(statsAfterInvalidJump.TopViewed, s => s.SlideId == 20 && s.ViewCount == 1);
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
