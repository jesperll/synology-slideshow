using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Services;
using SynologySlideshow.Api.Tests.TestDoubles;
using Xunit;

namespace SynologySlideshow.Api.Tests.Services;

public class ChannelPlaybackServiceLinkingTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"linking-test-{Guid.NewGuid()}.db");
    private readonly ServiceProvider _provider;
    private readonly FakeHubContext _hub = new();
    private readonly FakeSlideSource _slides = new();
    private readonly SyncGroupService _syncGroups = new();
    private readonly ChannelPlaybackService _playback;
    private readonly int _channelA;
    private readonly int _channelB;

    public ChannelPlaybackServiceLinkingTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<SlideshowDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        _provider = services.BuildServiceProvider();

        using (var scope = _provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<SlideshowDbContext>().Database.Migrate();
        }

        _playback = new ChannelPlaybackService(
            _slides,
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _hub,
            new FakeLogger<ChannelPlaybackService>(),
            _syncGroups);

        _slides.SlidesByAlbum[1] = new[] { new SlideRef(10), new SlideRef(20), new SlideRef(30) };
        _slides.SlidesByAlbum[2] = new[] { new SlideRef(100), new SlideRef(200) };

        using var setupScope = _provider.CreateScope();
        var db = setupScope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var a = new Channel { Name = "kitchen", NormalizedName = "KITCHEN", CurrentAlbumId = 1, IsPaused = false };
        var b = new Channel { Name = "living-room", NormalizedName = "LIVING-ROOM", CurrentAlbumId = 2, IsPaused = true };
        db.Channels.AddRange(a, b);
        db.SaveChanges();
        _channelA = a.Id;
        _channelB = b.Id;
    }

    [Fact]
    public async Task LinkingHarmonizesTheOtherMembersOntoTheSeedsAlbumSlideAndPauseState()
    {
        await _playback.AdvanceAsync(_channelA, 1); // seed now at slide 10

        var result = await _playback.LinkAsync(new[] { _channelA, _channelB });

        Assert.True(result.Success);
        var stateB = await _playback.GetStateAsync(_channelB);
        Assert.Equal(1, stateB.CurrentAlbumId);
        Assert.Equal(10, stateB.CurrentSlideId);
        Assert.False(stateB.IsPaused);
    }

    [Fact]
    public async Task TogglingPauseOnOneMemberSetsTheWholeGroupToTheSameNewValue()
    {
        await _playback.LinkAsync(new[] { _channelA, _channelB });

        await _playback.TogglePauseAsync(_channelA);

        var stateA = await _playback.GetStateAsync(_channelA);
        var stateB = await _playback.GetStateAsync(_channelB);
        Assert.True(stateA.IsPaused);
        Assert.True(stateB.IsPaused);
    }

    [Fact]
    public async Task AdvancingOneMemberAdvancesEveryMember()
    {
        await _playback.LinkAsync(new[] { _channelA, _channelB });

        var resultA = await _playback.AdvanceAsync(_channelA, 1);
        var stateB = await _playback.GetStateAsync(_channelB);

        Assert.Equal(10, resultA.CurrentSlideId);
        Assert.Equal(10, stateB.CurrentSlideId);
    }

    [Fact]
    public async Task LinkingFailsWhenAChannelDoesNotExist()
    {
        var result = await _playback.LinkAsync(new[] { _channelA, 999999 });

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task UnlinkingDissolvesTheWholeGroupAndEachContinuesIndependently()
    {
        await _playback.LinkAsync(new[] { _channelA, _channelB });

        await _playback.UnlinkAsync(_channelA);
        await _playback.AdvanceAsync(_channelA, 1);

        var stateB = await _playback.GetStateAsync(_channelB);
        Assert.Null(stateB.CurrentSlideId);
    }

    [Fact]
    public async Task OnlyTheLowestIdMembersTickActuallyAdvancesTheLinkedGroup()
    {
        await _playback.LinkAsync(new[] { _channelA, _channelB });

        _hub.Sent.Clear();
        await _playback.TickAsync(Math.Max(_channelA, _channelB));

        Assert.Empty(_hub.Sent);

        await _playback.TickAsync(Math.Min(_channelA, _channelB));

        var stateA = await _playback.GetStateAsync(_channelA);
        var stateB = await _playback.GetStateAsync(_channelB);
        Assert.Equal(10, stateA.CurrentSlideId);
        Assert.Equal(10, stateB.CurrentSlideId);
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
