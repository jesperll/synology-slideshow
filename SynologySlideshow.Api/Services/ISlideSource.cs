namespace SynologySlideshow.Api.Services;

public record SlideRef(int Id);

public interface ISlideSource
{
    SlideRef[] GetSlides(int albumId);

    // Gives the album a fresh random order for its next pass; the new order won't open with avoidFirstSlideId.
    void ReshuffleAlbum(int albumId, int avoidFirstSlideId);
}
