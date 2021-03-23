using Microsoft.AspNetCore.Mvc;
using SynologySlideshow.Api.Services;

namespace SynologySlideshow.Api.Controllers;

[ApiController]
[Route("api")]
public class ApiController : ControllerBase
{
    private readonly SlideShow _source;
    private readonly HttpClient _client;

    public ApiController(SlideShowService service, HttpClient client)
    {
        _source = service.SlideShow;
        _client = client;
    }

    [HttpGet("albums", Name = "Albums")]
    public IActionResult Albums()
    {
        var albums = _source.GetAlbums();
        if (albums == null)
            return NotFound();
        return Ok(albums.Select(a => new Album
        {
            Id = a.Id,
            Name = a.Name,
            Thumbnail = Url.RouteUrl("AlbumThumbnail", new { id = a.Id })
        }));
    }

    [HttpGet("albums/{id}")]
    public IActionResult Album(int id)
    {
        var a = _source.GetAlbum(id);
        if (a == null) return NotFound();
        return Ok(new Album { Id = a.Id, Name = a.Name, Thumbnail = Url.RouteUrl("AlbumThumbnail", new { id = a.Id }) });
    }

    [HttpGet("albums/{id}/thumbnail.jpg", Name = "AlbumThumbnail")]
    public async Task<IActionResult> AlbumThumbnail(int id)
    {
        var a = _source.GetAlbum(id);
        if (a == null) return NotFound();
        return File(await _client.GetStreamAsync(a.Thumbnail), "image/jpeg");
    }

    [HttpGet("albums/{id}/slides", Name = "AlbumSlides")]
    public IActionResult AlbumSlides(int id)
    {
        var slides = _source.GetSlides(id);
        return Ok(slides.Select(s => new Slide
        {
            Id = s.Id,
            Date = s.Date,
            Description = s.Description,
            Location = s.Location,
            Uri = Url.RouteUrl("SlidePhoto", new { id = id, slide = s.Id })
        }));
    }

    [HttpGet("albums/{id}/slides/{slide}.jpg", Name = "SlidePhoto")]
    public async Task<IActionResult> SlidePhoto(int id, int slide)
    {
        var a = _source.GetSlide(id, slide);
        if (a == null) return NotFound();
        return File(await _client.GetStreamAsync(a.Uri), "image/jpeg");
    }
}
