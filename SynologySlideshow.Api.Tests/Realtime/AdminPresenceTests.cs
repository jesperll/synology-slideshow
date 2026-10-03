using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SynologySlideshow.Api.Controllers;
using SynologySlideshow.Api.Realtime;
using Xunit;

namespace SynologySlideshow.Api.Tests.Realtime;

public class AdminPresenceTests : IClassFixture<SlideshowApiFactory>, IAsyncLifetime
{
    private readonly SlideshowApiFactory _factory;
    private HubConnection _viewer = null!;
    private HubConnection _admin = null!;

    public AdminPresenceTests(SlideshowApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _viewer = BuildConnection();
        _admin = BuildConnection();
        await _viewer.StartAsync();
        await _admin.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _viewer.DisposeAsync();
        await _admin.DisposeAsync();
    }

    private HubConnection BuildConnection() => BuildConnection(_factory);

    private static HubConnection BuildConnection(SlideshowApiFactory factory) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hub/slideshow"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            })
            .Build();

    private async Task<ChannelSummary> CreateChannelAsync(string name)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = name });
        return (await response.Content.ReadFromJsonAsync<ChannelSummary>())!;
    }

    [Fact]
    public async Task JoinAdminReturnsTheChannelWithItsCurrentViewerCount()
    {
        var channel = await CreateChannelAsync("presence-snapshot-test");
        await _viewer.InvokeAsync("JoinChannel", "presence-snapshot-test");

        var snapshot = await _admin.InvokeAsync<AdminSnapshot>("JoinAdmin");

        Assert.Contains(snapshot.Channels, c => c.ChannelId == channel.Id && c.ViewerCount == 1);
    }

    [Fact]
    public async Task AdminReceivesAPresenceUpdateWhenAViewerJoins()
    {
        var channel = await CreateChannelAsync("presence-push-test");
        await _admin.InvokeAsync("JoinAdmin");

        var updates = new List<AdminSnapshot>();
        _admin.On<AdminSnapshot>("PresenceChanged", updates.Add);

        await _viewer.InvokeAsync("JoinChannel", "presence-push-test");

        await WaitUntilAsync(() => updates.Any(s => s.Channels.Any(c => c.ChannelId == channel.Id && c.ViewerCount == 1)), TimeSpan.FromSeconds(5));
        Assert.Contains(updates, s => s.Channels.Any(c => c.ChannelId == channel.Id && c.ViewerCount == 1));
    }

    [Fact]
    public async Task AdminReceivesChannelStateChangedWhenAViewerControlsIt()
    {
        var channel = await CreateChannelAsync("presence-state-test");
        await _admin.InvokeAsync("JoinAdmin");
        // New channels start paused (Channel.IsPaused defaults to true), so compute the
        // expected post-toggle value dynamically instead of assuming false -> true.
        var initialState = await _viewer.InvokeAsync<ChannelStateDto?>("JoinChannel", "presence-state-test");
        var expectedPaused = !initialState!.IsPaused;

        var received = new List<ChannelStateDto>();
        _admin.On<ChannelStateDto>("ChannelStateChanged", received.Add);

        await _viewer.InvokeAsync("RequestTogglePause", channel.Id);

        await WaitUntilAsync(() => received.Any(s => s.ChannelId == channel.Id && s.IsPaused == expectedPaused), TimeSpan.FromSeconds(5));
        Assert.Contains(received, s => s.ChannelId == channel.Id && s.IsPaused == expectedPaused);
    }

    [Fact]
    public async Task DisconnectingRemovesTheViewerFromTheSnapshot()
    {
        var channel = await CreateChannelAsync("presence-disconnect-test");
        await _viewer.InvokeAsync("JoinChannel", "presence-disconnect-test");

        await _viewer.DisposeAsync();

        // The server's OnDisconnectedAsync runs asynchronously after the client disposes, so
        // poll until it has been processed instead of asserting on the very next read.
        AdminSnapshot snapshot = null!;
        await WaitUntilAsync(async () =>
        {
            snapshot = await _admin.InvokeAsync<AdminSnapshot>("JoinAdmin");
            return snapshot.Channels.Any(c => c.ChannelId == channel.Id && c.ViewerCount == 0);
        }, TimeSpan.FromSeconds(5));
        Assert.Contains(snapshot.Channels, c => c.ChannelId == channel.Id && c.ViewerCount == 0);
    }

    [Fact]
    public async Task AdminReceivesAPresenceUpdateWhenAChannelIsCreatedOverRest()
    {
        await _admin.InvokeAsync("JoinAdmin");
        var updates = new List<AdminSnapshot>();
        _admin.On<AdminSnapshot>("PresenceChanged", updates.Add);

        var channel = await CreateChannelAsync("presence-created-over-rest-test");

        await WaitUntilAsync(() => updates.Any(s => s.Channels.Any(c => c.ChannelId == channel.Id)), TimeSpan.FromSeconds(5));
        Assert.Contains(updates, s => s.Channels.Any(c => c.ChannelId == channel.Id));
    }

    [Fact]
    public async Task DeletingAChannelRemovesItFromTheSnapshot()
    {
        await using var isolatedFactory = new SlideshowApiFactory();
        var observer = BuildConnection(isolatedFactory);
        var viewer = BuildConnection(isolatedFactory);
        await observer.StartAsync();
        await viewer.StartAsync();
        try
        {
            await observer.InvokeAsync("JoinAdmin");
            var client = isolatedFactory.CreateClient();
            var response = await client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = "presence-delete-anon-test" });
            var channel = (await response.Content.ReadFromJsonAsync<ChannelSummary>())!;
            await viewer.InvokeAsync("JoinChannel", "presence-delete-anon-test");

            var beforeDelete = await client.GetFromJsonAsync<AdminSnapshot>("/api/admin/snapshot");
            Assert.Contains(beforeDelete!.Channels, c => c.ChannelId == channel.Id && c.ViewerCount == 1);

            await client.DeleteAsync($"/api/channels/{channel.Id}");

            var afterDelete = await client.GetFromJsonAsync<AdminSnapshot>("/api/admin/snapshot");
            Assert.DoesNotContain(afterDelete!.Channels, c => c.ChannelId == channel.Id);
        }
        finally
        {
            await viewer.DisposeAsync();
            await observer.DisposeAsync();
        }
    }

    [Fact]
    public async Task AdminReceivesAPresenceUpdateWhenAChannelIsDeletedOverRest()
    {
        var channel = await CreateChannelAsync("presence-deleted-over-rest-test");
        await _admin.InvokeAsync("JoinAdmin");
        var updates = new List<AdminSnapshot>();
        _admin.On<AdminSnapshot>("PresenceChanged", updates.Add);

        var client = _factory.CreateClient();
        await client.DeleteAsync($"/api/channels/{channel.Id}");

        await WaitUntilAsync(() => updates.Any(s => s.Channels.All(c => c.ChannelId != channel.Id)), TimeSpan.FromSeconds(5));
        Assert.Contains(updates, s => s.Channels.All(c => c.ChannelId != channel.Id));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!await condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }
}
