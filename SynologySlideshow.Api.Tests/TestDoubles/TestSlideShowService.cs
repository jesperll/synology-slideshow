using Microsoft.Extensions.Options;
using SynologySlideshow.Api.Services;

namespace SynologySlideshow.Api.Tests.TestDoubles;

public class TestSlideShowService : SlideShowService
{
    public TestSlideShowService() : base(Options.Create(new SynologyOptions
    {
        Uri = "https://example.invalid/webapi",
        Username = "test",
        Password = "test"
    }))
    {
    }

    public override Task InitAsync() => Task.CompletedTask;
}
