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

    public virtual async Task InitAsync()
    {
        var source = new SynologyAlbumSource(_options.Uri);
        await source.Login(_options.Username, _options.Password);
        var slideShow = new SlideShow(source);
        await slideShow.Refresh();
        SlideShow = slideShow;
    }

    // Re-fetches every album and slide from Synology, so newly-added or removed photos are
    // picked up without restarting the process. Called by MidnightRefreshService on a timer
    // and by AdminController's manual refresh endpoint.
    public virtual Task RefreshAsync() => SlideShow.Refresh();

    // Refresh keeps the existing order, so this is how an admin asks for a fresh random one.
    public virtual Task ShuffleAsync()
    {
        SlideShow.ReshuffleAll();
        return Task.CompletedTask;
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

    private readonly object _writeLock = new();

    // The very first load is shuffled; later refreshes keep each album's existing order and
    // only slot new photos in, so a channel's progress through its pass survives a refresh.
    public async Task Refresh()
    {
        var fetched = new List<(PhotoAlbum Album, PhotoSlide[] Slides)>();
        var albums = await _source.GetAlbums();
        foreach (var album in albums)
        {
            fetched.Add((album, await _source.GetSlides(album)));
        }

        lock (_writeLock)
        {
            var previous = _dict;
            var dict = new Dictionary<PhotoAlbum, PhotoSlide[]>();
            foreach (var (album, slides) in fetched)
            {
                var previousIds = previous?.FirstOrDefault(x => x.Key.Id == album.Id).Value?.Select(s => s.Id).ToArray()
                                  ?? Array.Empty<int>();
                dict.Add(album, SlideOrder.Merge(previousIds, slides, s => s.Id, Random.Shared));
            }
            _dict = dict;
        }
    }

    // Gives every album a brand-new random order. Channels carry on from wherever their
    // current photo lands in the new order.
    public void ReshuffleAll()
    {
        lock (_writeLock)
        {
            var current = _dict;
            if (current == null) return;

            var copy = new Dictionary<PhotoAlbum, PhotoSlide[]>(current.Count);
            foreach (var (album, slides) in current)
            {
                copy.Add(album, SlideOrder.Reshuffle(slides, s => s.Id, avoidFirstId: -1, Random.Shared));
            }
            _dict = copy;
        }
    }

    // Starts a new pass for one album with a fresh random order. Swaps in a new dictionary
    // rather than mutating, because readers enumerate _dict without taking the lock.
    public void Reshuffle(int albumId, int avoidFirstSlideId)
    {
        lock (_writeLock)
        {
            var current = _dict;
            var entry = current?.FirstOrDefault(x => x.Key.Id == albumId);
            if (current == null || entry?.Key == null) return;

            var copy = new Dictionary<PhotoAlbum, PhotoSlide[]>(current)
            {
                [entry.Value.Key] = SlideOrder.Reshuffle(entry.Value.Value, s => s.Id, avoidFirstSlideId, Random.Shared)
            };
            _dict = copy;
        }
    }

    private volatile Dictionary<PhotoAlbum, PhotoSlide[]>? _dict;
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
