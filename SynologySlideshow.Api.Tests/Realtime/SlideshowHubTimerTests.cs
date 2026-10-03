using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using SynologySlideshow.Api.Controllers;
using SynologySlideshow.Api.Realtime;
using SynologySlideshow.Api.Services;
using Xunit;

namespace SynologySlideshow.Api.Tests.Realtime;

// A channel's advance timer should only run while someone is actually watching it (directly,
// or via a sync-linked groupmate) - otherwise it wastes work ticking an empty room and
// inflates view stats for slides nobody saw. Uses its own factory per test (not the shared
// class fixture other hub tests use) so timer state from one test can't bleed into another.
public class SlideshowHubTimerTests
{
    private static HubConnection BuildConnection(SlideshowApiFactory factory) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hub/slideshow"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            })
            .Build();

    private static async Task<ChannelSummary> CreateChannelAsync(SlideshowApiFactory factory, string name)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = name });
        return (await response.Content.ReadFromJsonAsync<ChannelSummary>())!;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task ANewlyCreatedChannelHasNoRunningTimer()
    {
        await using var factory = new SlideshowApiFactory();
        var channel = await CreateChannelAsync(factory, "timer-fresh-channel");
        var playback = factory.Services.GetRequiredService<ChannelPlaybackService>();

        Assert.False(playback.IsTimerRunning(channel.Id));
    }

    [Fact]
    public async Task JoiningAChannelStartsItsTimer()
    {
        await using var factory = new SlideshowApiFactory();
        var channel = await CreateChannelAsync(factory, "timer-join-test");
        var playback = factory.Services.GetRequiredService<ChannelPlaybackService>();
        var connection = BuildConnection(factory);
        await connection.StartAsync();

        await connection.InvokeAsync("JoinChannel", "timer-join-test");

        Assert.True(playback.IsTimerRunning(channel.Id));

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task DisconnectingTheLastViewerStopsTheTimer()
    {
        await using var factory = new SlideshowApiFactory();
        var channel = await CreateChannelAsync(factory, "timer-disconnect-test");
        var playback = factory.Services.GetRequiredService<ChannelPlaybackService>();
        var connection = BuildConnection(factory);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinChannel", "timer-disconnect-test");
        Assert.True(playback.IsTimerRunning(channel.Id));

        await connection.DisposeAsync();

        // OnDisconnectedAsync runs asynchronously after the client disposes.
        await WaitUntilAsync(() => !playback.IsTimerRunning(channel.Id), TimeSpan.FromSeconds(5));
        Assert.False(playback.IsTimerRunning(channel.Id));
    }

    [Fact]
    public async Task LeavingTheLastViewerStopsTheTimer()
    {
        await using var factory = new SlideshowApiFactory();
        var channel = await CreateChannelAsync(factory, "timer-leave-test");
        var playback = factory.Services.GetRequiredService<ChannelPlaybackService>();
        var connection = BuildConnection(factory);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinChannel", "timer-leave-test");
        Assert.True(playback.IsTimerRunning(channel.Id));

        await connection.InvokeAsync("LeaveChannel", channel.Id);

        Assert.False(playback.IsTimerRunning(channel.Id));

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task AnotherRemainingViewerKeepsTheTimerRunning()
    {
        await using var factory = new SlideshowApiFactory();
        var channel = await CreateChannelAsync(factory, "timer-two-viewers-test");
        var playback = factory.Services.GetRequiredService<ChannelPlaybackService>();
        var connectionA = BuildConnection(factory);
        var connectionB = BuildConnection(factory);
        await connectionA.StartAsync();
        await connectionB.StartAsync();
        await connectionA.InvokeAsync("JoinChannel", "timer-two-viewers-test");
        await connectionB.InvokeAsync("JoinChannel", "timer-two-viewers-test");

        await connectionA.DisposeAsync();

        // Give the disconnect handler a moment, then confirm the timer survived it.
        await Task.Delay(200);
        Assert.True(playback.IsTimerRunning(channel.Id));

        await connectionB.DisposeAsync();
    }

    [Fact]
    public async Task ALinkedGroupmatesViewerKeepsAViewerlessMembersTimerRunning()
    {
        await using var factory = new SlideshowApiFactory();
        var idle = await CreateChannelAsync(factory, "timer-linked-idle-test");
        var watched = await CreateChannelAsync(factory, "timer-linked-watched-test");
        var playback = factory.Services.GetRequiredService<ChannelPlaybackService>();
        var connection = BuildConnection(factory);
        await connection.StartAsync();

        await connection.InvokeAsync("RequestLinkChannels", new[] { idle.Id, watched.Id });
        await connection.InvokeAsync("JoinChannel", "timer-linked-watched-test");

        // idle has 0 viewers of its own, but its groupmate "watched" has one - the group as a
        // whole is watched, so idle's timer (which drives nothing on its own, but must still
        // run in case it's ever the lower id) stays running too.
        Assert.True(playback.IsTimerRunning(idle.Id));
        Assert.True(playback.IsTimerRunning(watched.Id));

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task UnlinkingStopsTheNowIdleMembersTimer()
    {
        await using var factory = new SlideshowApiFactory();
        var idle = await CreateChannelAsync(factory, "timer-unlink-idle-test");
        var watched = await CreateChannelAsync(factory, "timer-unlink-watched-test");
        var playback = factory.Services.GetRequiredService<ChannelPlaybackService>();
        var connection = BuildConnection(factory);
        await connection.StartAsync();
        await connection.InvokeAsync("RequestLinkChannels", new[] { idle.Id, watched.Id });
        await connection.InvokeAsync("JoinChannel", "timer-unlink-watched-test");
        Assert.True(playback.IsTimerRunning(idle.Id));

        await connection.InvokeAsync("RequestUnlinkChannel", idle.Id);

        Assert.False(playback.IsTimerRunning(idle.Id));
        Assert.True(playback.IsTimerRunning(watched.Id));

        await connection.DisposeAsync();
    }
}
