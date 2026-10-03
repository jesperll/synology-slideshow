using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SynologySlideshow.Api.Controllers;
using SynologySlideshow.Api.Services;
using Xunit;

namespace SynologySlideshow.Api.Tests.Controllers;

public class ChannelStatsControllerTests : IClassFixture<SlideshowApiFactory>
{
    private readonly SlideshowApiFactory _factory;
    private readonly HttpClient _client;

    public ChannelStatsControllerTests(SlideshowApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ReturnsRecordedViewsForTheChannel()
    {
        var created = await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = "stats-endpoint-test" });
        var channel = await created.Content.ReadFromJsonAsync<ChannelSummary>();

        using (var scope = _factory.Services.CreateScope())
        {
            var viewStats = scope.ServiceProvider.GetRequiredService<ViewStatsService>();
            await viewStats.RecordViewAsync(channel!.Id, 10);
            await viewStats.RecordViewAsync(channel.Id, 10);
            await viewStats.RecordViewAsync(channel.Id, 20);
        }

        var stats = await _client.GetFromJsonAsync<ChannelStats>($"/api/channels/{channel!.Id}/stats");

        Assert.Equal(3, stats!.TotalViews);
        Assert.Contains(stats.TopViewed, s => s.SlideId == 10 && s.ViewCount == 2);
    }

    [Fact]
    public async Task ReturnsNotFoundForAnUnknownChannel()
    {
        var response = await _client.GetAsync("/api/channels/999999/stats");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
