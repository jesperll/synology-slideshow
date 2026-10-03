using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using SynologySlideshow.Api.Controllers;
using SynologySlideshow.Api.Realtime;
using SynologySlideshow.Api.Services;
using Xunit;

namespace SynologySlideshow.Api.Tests.Realtime;

public class SlideshowHubLinkingTests : IClassFixture<SlideshowApiFactory>, IAsyncLifetime
{
    private readonly SlideshowApiFactory _factory;
    private HubConnection _connectionA = null!;
    private HubConnection _connectionB = null!;

    public SlideshowHubLinkingTests(SlideshowApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _connectionA = BuildConnection();
        _connectionB = BuildConnection();
        await _connectionA.StartAsync();
        await _connectionB.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _connectionA.DisposeAsync();
        await _connectionB.DisposeAsync();
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
    public async Task LinkingTwoChannelsThenTogglingPauseOnOneTogglesBoth()
    {
        var channelA = await CreateChannelAsync("link-hub-a");
        var channelB = await CreateChannelAsync("link-hub-b");

        var linkResult = await _connectionA.InvokeAsync<LinkResult>("RequestLinkChannels", new[] { channelA.Id, channelB.Id });
        Assert.True(linkResult.Success);

        var stateA = await _connectionA.InvokeAsync<ChannelStateDto?>("JoinChannel", "link-hub-a");
        await _connectionB.InvokeAsync("JoinChannel", "link-hub-b");
        // New channels start paused, so the toggle's direction depends on the starting state.
        var expectedPaused = !stateA!.IsPaused;

        var receivedByB = new List<ChannelStateDto>();
        _connectionB.On<ChannelStateDto>("ChannelStateChanged", receivedByB.Add);

        await _connectionA.InvokeAsync("RequestTogglePause", channelA.Id);

        await WaitUntilAsync(() => receivedByB.Any(s => s.ChannelId == channelB.Id && s.IsPaused == expectedPaused), TimeSpan.FromSeconds(5));
        Assert.Contains(receivedByB, s => s.ChannelId == channelB.Id && s.IsPaused == expectedPaused);
    }

    [Fact]
    public async Task UnlinkingReturnsAChannelToIndependentControl()
    {
        var channelA = await CreateChannelAsync("unlink-hub-a");
        var channelB = await CreateChannelAsync("unlink-hub-b");
        await _connectionA.InvokeAsync<LinkResult>("RequestLinkChannels", new[] { channelA.Id, channelB.Id });

        await _connectionA.InvokeAsync("RequestUnlinkChannel", channelA.Id);

        // B must actually be in its channel group, or it would receive nothing whether or not the unlink worked.
        await _connectionB.InvokeAsync("JoinChannel", "unlink-hub-b");

        var receivedByB = new List<ChannelStateDto>();
        _connectionB.On<ChannelStateDto>("ChannelStateChanged", receivedByB.Add);

        await _connectionA.InvokeAsync("RequestTogglePause", channelA.Id);
        await Task.Delay(200);

        Assert.DoesNotContain(receivedByB, s => s.ChannelId == channelB.Id);
    }

    [Fact]
    public async Task DeletingALinkedChannelLeavesTheSurvivorUnlinkedAndControllable()
    {
        var survivor = await CreateChannelAsync("delete-link-survivor");
        var doomed = await CreateChannelAsync("delete-link-doomed");
        var linkResult = await _connectionA.InvokeAsync<LinkResult>("RequestLinkChannels", new[] { survivor.Id, doomed.Id });
        Assert.True(linkResult.Success);

        var deleteResponse = await _factory.CreateClient().DeleteAsync($"/api/channels/{doomed.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var state = await _connectionA.InvokeAsync<ChannelStateDto>("RequestTogglePause", survivor.Id);
        Assert.Equal(survivor.Id, state.ChannelId);

        var playback = _factory.Services.GetRequiredService<ChannelPlaybackService>();
        Assert.Equal(new[] { survivor.Id }, playback.GetGroupMembers(survivor.Id).ToArray());
    }

    [Fact]
    public async Task LinkingChannelsBroadcastsAPresenceUpdateWithTheLinkedIds()
    {
        var channelA = await CreateChannelAsync("link-presence-a");
        var channelB = await CreateChannelAsync("link-presence-b");
        await _connectionB.InvokeAsync("JoinAdmin");

        var updates = new List<AdminSnapshot>();
        _connectionB.On<AdminSnapshot>("PresenceChanged", updates.Add);

        var linkResult = await _connectionA.InvokeAsync<LinkResult>("RequestLinkChannels", new[] { channelA.Id, channelB.Id });
        Assert.True(linkResult.Success);

        await WaitUntilAsync(() => updates.Any(s =>
            s.Channels.Any(c => c.ChannelId == channelA.Id && c.LinkedChannelIds.Contains(channelB.Id)) &&
            s.Channels.Any(c => c.ChannelId == channelB.Id && c.LinkedChannelIds.Contains(channelA.Id))),
            TimeSpan.FromSeconds(5));

        Assert.Contains(updates, s =>
            s.Channels.Any(c => c.ChannelId == channelA.Id && c.LinkedChannelIds.Contains(channelB.Id)) &&
            s.Channels.Any(c => c.ChannelId == channelB.Id && c.LinkedChannelIds.Contains(channelA.Id)));
    }

    [Fact]
    public async Task UnlinkingChannelsBroadcastsAPresenceUpdateWithEmptyLinkedIds()
    {
        var channelA = await CreateChannelAsync("unlink-presence-a");
        var channelB = await CreateChannelAsync("unlink-presence-b");
        await _connectionA.InvokeAsync<LinkResult>("RequestLinkChannels", new[] { channelA.Id, channelB.Id });

        await _connectionB.InvokeAsync("JoinAdmin");
        var updates = new List<AdminSnapshot>();
        _connectionB.On<AdminSnapshot>("PresenceChanged", updates.Add);

        await _connectionA.InvokeAsync("RequestUnlinkChannel", channelA.Id);

        await WaitUntilAsync(() => updates.Any(s =>
            s.Channels.Any(c => c.ChannelId == channelA.Id && c.LinkedChannelIds.Length == 0) &&
            s.Channels.Any(c => c.ChannelId == channelB.Id && c.LinkedChannelIds.Length == 0)),
            TimeSpan.FromSeconds(5));

        Assert.Contains(updates, s =>
            s.Channels.Any(c => c.ChannelId == channelA.Id && c.LinkedChannelIds.Length == 0) &&
            s.Channels.Any(c => c.ChannelId == channelB.Id && c.LinkedChannelIds.Length == 0));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }
}
