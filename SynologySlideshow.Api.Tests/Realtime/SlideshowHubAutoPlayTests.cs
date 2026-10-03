using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SynologySlideshow.Api.Controllers;
using SynologySlideshow.Api.Realtime;
using Xunit;

namespace SynologySlideshow.Api.Tests.Realtime;

// A client joining a paused channel almost always means "nobody told it to play yet" (every
// channel starts paused) rather than a pause someone wants preserved for the new viewer.
public class SlideshowHubAutoPlayTests : IAsyncLifetime
{
    private SlideshowApiFactory _factory = null!;
    private HubConnection _connectionA = null!;
    private HubConnection _connectionB = null!;

    public async Task InitializeAsync()
    {
        _factory = new SlideshowApiFactory();
        _connectionA = BuildConnection();
        _connectionB = BuildConnection();
        await _connectionA.StartAsync();
        await _connectionB.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _connectionA.DisposeAsync();
        await _connectionB.DisposeAsync();
        await _factory.DisposeAsync();
    }

    private HubConnection BuildConnection() =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hub/slideshow"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

    private async Task<ChannelSummary> CreateChannelAsync(string name)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = name });
        return (await response.Content.ReadFromJsonAsync<ChannelSummary>())!;
    }

    [Fact]
    public async Task JoiningAFreshChannelStartsItPlaying()
    {
        await CreateChannelAsync("autoplay-fresh-test");

        var state = await _connectionA.InvokeAsync<ChannelStateDto?>("JoinChannel", "autoplay-fresh-test");

        Assert.False(state!.IsPaused);
    }

    [Fact]
    public async Task JoiningAnAlreadyPlayingChannelLeavesItPlaying()
    {
        await CreateChannelAsync("autoplay-already-playing-test");
        await _connectionA.InvokeAsync("JoinChannel", "autoplay-already-playing-test");

        var state = await _connectionB.InvokeAsync<ChannelStateDto?>("JoinChannel", "autoplay-already-playing-test");

        Assert.False(state!.IsPaused);
    }

    [Fact]
    public async Task JoiningAChannelAnAdminDeliberatelyRepausedStillResumesIt()
    {
        var channel = await CreateChannelAsync("autoplay-repause-test");
        await _connectionA.InvokeAsync("JoinChannel", "autoplay-repause-test");
        // Channel is now playing (per the fresh-join behavior); pause it, simulating an admin
        // deliberately holding on the current slide.
        await _connectionA.InvokeAsync("RequestTogglePause", channel.Id);

        var state = await _connectionB.InvokeAsync<ChannelStateDto?>("JoinChannel", "autoplay-repause-test");

        Assert.False(state!.IsPaused);
    }

    [Fact]
    public async Task JoiningOneMemberOfAPausedLinkedGroupResumesTheWholeGroup()
    {
        var a = await CreateChannelAsync("autoplay-linked-a-test");
        var b = await CreateChannelAsync("autoplay-linked-b-test");
        await _connectionA.InvokeAsync("RequestLinkChannels", new[] { a.Id, b.Id });

        var stateA = await _connectionA.InvokeAsync<ChannelStateDto?>("JoinChannel", "autoplay-linked-a-test");
        Assert.False(stateA!.IsPaused);

        // b was never joined directly, but TogglePauseAsync (triggered by a's join) applies to
        // the whole linked group, so b should already be unpaused too.
        var stateB = await _connectionB.InvokeAsync<ChannelStateDto?>("JoinChannel", "autoplay-linked-b-test");
        Assert.False(stateB!.IsPaused);
    }
}
