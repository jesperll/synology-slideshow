using Microsoft.Extensions.Options;
using SynologySlideshow.Core;
using SynologySlideshow.Core.Models;

namespace SynologySlideshow.Api.Services;

public class SlideShowService
{
    private readonly SynologyOptions _options;

    public SlideShowService(IOptions<SynologyOptions> options)
    {
        _options = options.Value;
    }

    public async Task InitAsync()
    {
        var source = new SynologyAlbumSource(_options.Uri);
        await source.Login(_options.Username, _options.Password);
        var slideShow = new SlideShow(source);
        await slideShow.Refresh();
        SlideShow = slideShow;
    }

    public SlideShow SlideShow { get; private set; } = null!;
}

public sealed class SynologyOptions
{
    public string Uri { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
}

public class SlideShow
{
    private readonly SynologyAlbumSource _source;

    public SlideShow(SynologyAlbumSource source)
    {
        _source = source;
    }

    void Shuffle(PhotoSlide[] slides)
    {
        Random random = new Random();
        // Fisher-Yates Shuffle
        for (var i = slides.Length; i > 1; i--)
        {
            var j = random.Next(i);
            (slides[j], slides[i - 1]) = (slides[i - 1], slides[j]);
        }
    }

    public async Task Refresh()
    {
        Dictionary<PhotoAlbum, PhotoSlide[]> dict = new Dictionary<PhotoAlbum, PhotoSlide[]>();
        var albums = await _source.GetAlbums();
        foreach (var album in albums)
        {
            var slides = await _source.GetSlides(album);
            //Shuffle(slides);
            dict.Add(album, slides);
        }
        _dict = dict;
    }

    private Dictionary<PhotoAlbum, PhotoSlide[]>? _dict;
    public IEnumerable<PhotoAlbum>? GetAlbums() => _dict?.Keys;
    public PhotoAlbum? GetAlbum(int id) => _dict?.Keys.FirstOrDefault(x => x.Id == id);

    public PhotoSlide[] GetSlides(int albumId)
    {
        return _dict?.FirstOrDefault(x => x.Key.Id == albumId).Value ?? Array.Empty<PhotoSlide>();
    }
    public PhotoSlide? GetSlide(int albumId, int slideId)
    {
        return GetSlides(albumId).FirstOrDefault(x => x.Id == slideId);
    }
}
