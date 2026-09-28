using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SynologySlideshow.Api.Controllers;
using SynologySlideshow.Api.Realtime;
using Xunit;

namespace SynologySlideshow.Api.Tests.Realtime;

public class SlideshowHubTests : IClassFixture<SlideshowApiFactory>, IAsyncLifetime
{
    private readonly SlideshowApiFactory _factory;
    private HubConnection _connectionA = null!;
    private HubConnection _connectionB = null!;

    public SlideshowHubTests(SlideshowApiFactory factory)
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
    public async Task JoiningAnExistingChannelReturnsItsCurrentState()
    {
        var channel = await CreateChannelAsync("hub-join-test");

        var state = await _connectionA.InvokeAsync<ChannelStateDto?>("JoinChannel", "hub-join-test");

        Assert.NotNull(state);
        Assert.Equal(channel.Id, state!.ChannelId);
    }

    [Fact]
    public async Task JoiningAnUnknownChannelReturnsNull()
    {
        var state = await _connectionA.InvokeAsync<ChannelStateDto?>("JoinChannel", "does-not-exist");

        Assert.Null(state);
    }

    [Fact]
    public async Task ControllingOneConnectionBroadcastsToEveryConnectionInTheChannel()
    {
        var channel = await CreateChannelAsync("hub-broadcast-test");

        var receivedByB = new List<ChannelStateDto>();
        _connectionB.On<ChannelStateDto>("ChannelStateChanged", dto => receivedByB.Add(dto));

        var initialState = await _connectionA.InvokeAsync<ChannelStateDto?>("JoinChannel", "hub-broadcast-test");
        await _connectionB.InvokeAsync("JoinChannel", "hub-broadcast-test");

        var expectedPaused = !initialState!.IsPaused;

        await _connectionA.InvokeAsync("RequestTogglePause", channel.Id);

        await WaitUntilAsync(() => receivedByB.Any(s => s.IsPaused == expectedPaused), TimeSpan.FromSeconds(5));
        Assert.Contains(receivedByB, s => s.IsPaused == expectedPaused);
    }

    [Fact]
    public async Task RequestingAControlOnAnUnknownChannelFaultsOnlyThatCallNotTheConnection()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => _connectionA.InvokeAsync<ChannelStateDto>("RequestNextSlide", 999999));

        // the connection must still be usable afterwards
        var channel = await CreateChannelAsync("hub-recovers-test");
        var state = await _connectionA.InvokeAsync<ChannelStateDto?>("JoinChannel", "hub-recovers-test");
        Assert.NotNull(state);
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
