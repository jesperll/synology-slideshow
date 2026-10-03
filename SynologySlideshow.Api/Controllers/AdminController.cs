using Microsoft.AspNetCore.Mvc;
using SynologySlideshow.Api.Services;

namespace SynologySlideshow.Api.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly AdminSnapshotService _snapshotService;
    private readonly SlideShowService _slideShowService;

    public AdminController(AdminSnapshotService snapshotService, SlideShowService slideShowService)
    {
        _snapshotService = snapshotService;
        _slideShowService = slideShowService;
    }

    [HttpGet("snapshot")]
    public async Task<IActionResult> GetSnapshot() => Ok(await _snapshotService.BuildAsync());

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        await _slideShowService.RefreshAsync();
        return NoContent();
    }
}
