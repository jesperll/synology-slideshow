using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SynologySlideshow.Api.Services;
using SynologySlideshow.Api.Tests.TestDoubles;
using Xunit;

namespace SynologySlideshow.Api.Tests.Controllers;

public class AdminControllerTests : IClassFixture<SlideshowApiFactory>
{
    private readonly SlideshowApiFactory _factory;
    private readonly HttpClient _client;

    public AdminControllerTests(SlideshowApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RefreshReturnsNoContentAndRefreshesTheLibrary()
    {
        var slideShowService = (TestSlideShowService)_factory.Services.GetRequiredService<SlideShowService>();
        var callsBefore = slideShowService.RefreshCallCount;

        var response = await _client.PostAsync("/api/admin/refresh", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(callsBefore + 1, slideShowService.RefreshCallCount);
    }
}
