using SynologySlideshow.Api.Services;

namespace SynologySlideshow.Api.Tests.TestDoubles;

public class FakeSlideSource : ISlideSource
{
    public Dictionary<int, SlideRef[]> SlidesByAlbum { get; } = new();

    public SlideRef[] GetSlides(int albumId) =>
        SlidesByAlbum.TryGetValue(albumId, out var slides) ? slides : Array.Empty<SlideRef>();
}
