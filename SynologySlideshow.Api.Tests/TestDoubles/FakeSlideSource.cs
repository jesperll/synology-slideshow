using SynologySlideshow.Api.Services;

namespace SynologySlideshow.Api.Tests.TestDoubles;

public class FakeSlideSource : ISlideSource
{
    public Dictionary<int, SlideRef[]> SlidesByAlbum { get; } = new();

    public List<(int AlbumId, int AvoidFirstSlideId)> ReshuffleCalls { get; } = new();

    // When set, ReshuffleAlbum replaces the album's order with this one (a stand-in for "a new random order").
    public int[]? NextReshuffledOrder { get; set; }

    public SlideRef[] GetSlides(int albumId) =>
        SlidesByAlbum.TryGetValue(albumId, out var slides) ? slides : Array.Empty<SlideRef>();

    public void ReshuffleAlbum(int albumId, int avoidFirstSlideId)
    {
        ReshuffleCalls.Add((albumId, avoidFirstSlideId));
        if (NextReshuffledOrder != null)
        {
            SlidesByAlbum[albumId] = NextReshuffledOrder.Select(id => new SlideRef(id)).ToArray();
        }
    }
}
