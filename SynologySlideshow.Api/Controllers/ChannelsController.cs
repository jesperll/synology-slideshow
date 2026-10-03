using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Realtime;
using SynologySlideshow.Api.Services;

namespace SynologySlideshow.Api.Controllers;

[ApiController]
[Route("api/channels")]
public class ChannelsController : ControllerBase
{
    private static readonly string[] ReservedNames = { "ADMIN", "DEFAULT" };
    // Channel names become a single URL path segment, so only allow characters that are safe there.
    private static readonly Regex ValidName = new(@"^[\p{L}\p{N} _-]{1,64}$", RegexOptions.Compiled);

    private readonly SlideshowDbContext _db;
    private readonly ChannelPlaybackService _playback;
    private readonly IHubContext<SlideshowHub> _hubContext;
    private readonly AdminSnapshotService _snapshotService;
    private readonly PresenceTracker _presence;
    private readonly ILogger<ChannelsController> _logger;
    private readonly ViewStatsService _viewStats;

    public ChannelsController(
        SlideshowDbContext db,
        ChannelPlaybackService playback,
        IHubContext<SlideshowHub> hubContext,
        AdminSnapshotService snapshotService,
        PresenceTracker presence,
        ILogger<ChannelsController> logger,
        ViewStatsService viewStats)
    {
        _db = db;
        _playback = playback;
        _hubContext = hubContext;
        _snapshotService = snapshotService;
        _presence = presence;
        _logger = logger;
        _viewStats = viewStats;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var channels = await _db.Channels
            .OrderBy(c => c.Name)
            .Select(c => new ChannelSummary { Id = c.Id, Name = c.Name, IsDefault = c.IsDefault })
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

        if (!ValidName.IsMatch(name))
            return BadRequest("Name must be 1-64 characters and contain only letters, digits, spaces, underscores and hyphens.");

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
        await BroadcastPresenceAsync();
        return CreatedAtAction(nameof(List), new ChannelSummary { Id = channel.Id, Name = channel.Name });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var channel = await _db.Channels.FindAsync(id);
        if (channel == null) return NotFound();
        if (channel.IsDefault) return BadRequest("The default channel can't be deleted.");

        // Dissolve any sync group first (UnlinkAsync reads the channel row, so it must still exist);
        // otherwise surviving members would keep fanning out to this deleted id and throw.
        await _playback.UnlinkAsync(id);

        _db.Channels.Remove(channel);
        await _db.SaveChangesAsync();
        _presence.RemoveChannel(id);
        _playback.StopTimer(id);
        await BroadcastPresenceAsync();
        return NoContent();
    }

    [HttpGet("{id}/stats")]
    public async Task<IActionResult> GetStats(int id)
    {
        if (!await _db.Channels.AnyAsync(c => c.Id == id)) return NotFound();
        return Ok(await _viewStats.GetStatsAsync(id));
    }

    // Best-effort: the mutation has already been saved, so a broadcast failure must not turn
    // a successful create/delete into an error response.
    private async Task BroadcastPresenceAsync()
    {
        try
        {
            var snapshot = await _snapshotService.BuildAsync();
            await _hubContext.Clients.Group(SlideshowHub.AdminGroupName).SendAsync("PresenceChanged", snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast presence update to admins");
        }
    }
}
