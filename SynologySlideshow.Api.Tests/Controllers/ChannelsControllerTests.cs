using System.Net;
using System.Net.Http.Json;
using SynologySlideshow.Api.Controllers;
using Xunit;

namespace SynologySlideshow.Api.Tests.Controllers;

public class ChannelsControllerTests : IClassFixture<SlideshowApiFactory>
{
    private readonly HttpClient _client;

    public ChannelsControllerTests(SlideshowApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateThenListReturnsTheNewChannel()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = "kitchen" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var listResponse = await _client.GetFromJsonAsync<List<ChannelSummary>>("/api/channels");
        Assert.Contains(listResponse!, c => c.Name == "kitchen");
    }

    [Fact]
    public async Task CreateWithDuplicateNameReturnsConflict()
    {
        await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = "living-room" });
        var response = await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = "living-room" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateWithDifferentCasingOfExistingNameReturnsConflict()
    {
        await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = "Office" });
        var response = await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = " office " });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("Admin")]
    [InlineData(" ADMIN ")]
    public async Task CreateWithReservedNameReturnsBadRequest(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("living/room")]
    [InlineData("what?")]
    [InlineData("room#1")]
    public async Task CreateWithUnsafeCharactersReturnsBadRequest(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateWithNameLongerThan64CharactersReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = new string('a', 65) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAllowsAccentedLettersHyphensAndSpaces()
    {
        var response = await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = "café-room 2" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRemovesTheChannel()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = "bedroom" });
        var created = await createResponse.Content.ReadFromJsonAsync<ChannelSummary>();

        var deleteResponse = await _client.DeleteAsync($"/api/channels/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await _client.GetFromJsonAsync<List<ChannelSummary>>("/api/channels");
        Assert.DoesNotContain(listResponse!, c => c.Id == created.Id);
    }

    [Fact]
    public async Task DeleteOfUnknownIdReturnsNotFound()
    {
        var response = await _client.DeleteAsync("/api/channels/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
