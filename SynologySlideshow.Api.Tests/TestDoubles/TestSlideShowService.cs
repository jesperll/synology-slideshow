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

    public int RefreshCallCount { get; private set; }

    // Base RefreshAsync calls SlideShow.Refresh(), but SlideShow is never set here since
    // InitAsync is a no-op - overridden to avoid a NullReferenceException in tests that hit
    // the manual refresh endpoint, while still letting tests assert it was called.
    public override Task RefreshAsync()
    {
        RefreshCallCount++;
        return Task.CompletedTask;
    }
}
