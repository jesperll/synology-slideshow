namespace SynologySlideshow.Api.Services;

public record SlideRef(int Id);

public interface ISlideSource
{
    SlideRef[] GetSlides(int albumId);
}
