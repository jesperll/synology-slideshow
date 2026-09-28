using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Services;

namespace SynologySlideshow.Api.Controllers;

[ApiController]
[Route("api/channels")]
public class ChannelsController : ControllerBase
{
    private static readonly string[] ReservedNames = { "ADMIN" };

    private readonly SlideshowDbContext _db;
    private readonly ChannelPlaybackService _playback;

    public ChannelsController(SlideshowDbContext db, ChannelPlaybackService playback)
    {
        _db = db;
        _playback = playback;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var channels = await _db.Channels
            .OrderBy(c => c.Name)
            .Select(c => new ChannelSummary { Id = c.Id, Name = c.Name })
            .ToListAsync();
        return Ok(channels);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateChannelRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");

        var name = request.Name.Trim();
        var normalized = name.ToUpperInvariant();

        if (ReservedNames.Contains(normalized))
            return BadRequest($"'{name}' is a reserved name and can't be used for a channel.");

        if (await _db.Channels.AnyAsync(c => c.NormalizedName == normalized))
            return Conflict($"A channel named '{name}' already exists.");

        var channel = new Channel { Name = name, NormalizedName = normalized };
        _db.Channels.Add(channel);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict($"A channel named '{name}' already exists.");
        }

        _playback.StartTimer(channel.Id);
        return CreatedAtAction(nameof(List), new ChannelSummary { Id = channel.Id, Name = channel.Name });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var channel = await _db.Channels.FindAsync(id);
        if (channel == null) return NotFound();

        _db.Channels.Remove(channel);
        await _db.SaveChangesAsync();
        _playback.StopTimer(id);
        return NoContent();
    }
}
