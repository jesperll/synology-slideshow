namespace SynologySlideshow.Api.Services;

public class SlideShowSlideSource : ISlideSource
{
    private readonly SlideShowService _slideShowService;

    public SlideShowSlideSource(SlideShowService slideShowService)
    {
        _slideShowService = slideShowService;
    }

    public SlideRef[] GetSlides(int albumId) =>
        _slideShowService.SlideShow.GetSlides(albumId).Select(s => new SlideRef(s.Id)).ToArray();

    public void ReshuffleAlbum(int albumId, int avoidFirstSlideId) =>
        _slideShowService.SlideShow.Reshuffle(albumId, avoidFirstSlideId);
}
