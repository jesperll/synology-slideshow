using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Realtime;
using SynologySlideshow.Api.Services;
using SynologySlideshow.Api.Tests.TestDoubles;
using Xunit;

namespace SynologySlideshow.Api.Tests.Services;

public class ChannelPlaybackServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"playback-test-{Guid.NewGuid()}.db");
    private readonly ServiceProvider _provider;
    private readonly FakeHubContext _hub = new();
    private readonly FakeSlideSource _slides = new();
    private readonly FakeLogger<ChannelPlaybackService> _logger = new();
    private readonly ChannelPlaybackService _playback;
    private readonly int _channelId;

    public ChannelPlaybackServiceTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<SlideshowDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        _provider = services.BuildServiceProvider();

        using (var scope = _provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<SlideshowDbContext>().Database.Migrate();
        }

        _playback = new ChannelPlaybackService(_slides, _provider.GetRequiredService<IServiceScopeFactory>(), _hub, _logger);

        _slides.SlidesByAlbum[1] = new[] { new SlideRef(10), new SlideRef(20), new SlideRef(30) };

        using var setupScope = _provider.CreateScope();
        var db = setupScope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = new Channel { Name = "kitchen", NormalizedName = "KITCHEN", CurrentAlbumId = 1, IsPaused = false };
        db.Channels.Add(channel);
        db.SaveChanges();
        _channelId = channel.Id;
    }

    [Fact]
    public async Task AdvanceMovesToNextSlideAndWrapsAround()
    {
        var first = await _playback.AdvanceAsync(_channelId, 1);
        Assert.Equal(10, first.CurrentSlideId);

        var second = await _playback.AdvanceAsync(_channelId, 1);
        Assert.Equal(20, second.CurrentSlideId);

        await _playback.AdvanceAsync(_channelId, 1); // -> 30
        var wrapped = await _playback.AdvanceAsync(_channelId, 1);
        Assert.Equal(10, wrapped.CurrentSlideId);
    }

    [Fact]
    public async Task JumpToSlideSetsExactSlide()
    {
        var state = await _playback.JumpAsync(_channelId, 30);
        Assert.Equal(30, state.CurrentSlideId);
    }

    [Fact]
    public async Task TogglePausePersistsAcrossReload()
    {
        var toggled = await _playback.TogglePauseAsync(_channelId);
        Assert.True(toggled.IsPaused);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var reloaded = await db.Channels.FindAsync(_channelId);
        Assert.True(reloaded!.IsPaused);
    }

    [Fact]
    public async Task SwitchingToAnAlbumWithNoSlidesResolvesToNoCurrentSlide()
    {
        var state = await _playback.SetAlbumAsync(_channelId, albumId: 999);
        Assert.Null(state.CurrentSlideId);
    }

    [Fact]
    public async Task EveryMutationBroadcastsToTheChannelGroup()
    {
        await _playback.AdvanceAsync(_channelId, 1);

        Assert.Contains(_hub.Sent, s => s.Group == SlideshowHub.GroupName(_channelId) && s.Method == "ChannelStateChanged");
    }

    [Fact]
    public async Task TickDoesNothingWhilePaused()
    {
        await _playback.TogglePauseAsync(_channelId); // now paused
        _hub.Sent.Clear();

        await _playback.TickAsync(_channelId);

        Assert.Empty(_hub.Sent);
    }

    [Fact]
    public async Task TickAdvancesOneSlideWhilePlaying()
    {
        await _playback.TickAsync(_channelId);
        var state = await _playback.GetStateAsync(_channelId);

        Assert.Equal(10, state.CurrentSlideId);
    }

    [Fact]
    public async Task StateSurvivesAFreshServiceInstanceOverTheSameDatabase()
    {
        await _playback.TogglePauseAsync(_channelId); // persist IsPaused = true

        var freshPlayback = new ChannelPlaybackService(_slides, _provider.GetRequiredService<IServiceScopeFactory>(), _hub, _logger);
        _hub.Sent.Clear();

        await freshPlayback.TickAsync(_channelId);

        Assert.Empty(_hub.Sent); // still paused, a fresh in-process runtime must not lose that
    }

    [Fact]
    public async Task TimerDrivenTickLogsAndSwallowsExceptionsInsteadOfCrashingTheTimer()
    {
        var throwingPlayback = new ChannelPlaybackService(new ThrowingSlideSource(), _provider.GetRequiredService<IServiceScopeFactory>(), _hub, _logger);

        // OnTick is the private callback the System.Threading.Timer invokes; drive it directly
        // via reflection rather than waiting out the real 30s AdvanceInterval.
        var onTick = typeof(ChannelPlaybackService).GetMethod("OnTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        onTick.Invoke(throwingPlayback, new object?[] { _channelId });

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (_logger.Entries.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, entry.LogLevel);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }

    private class ThrowingSlideSource : ISlideSource
    {
        public SlideRef[] GetSlides(int albumId) => throw new InvalidOperationException("boom");
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
