using Microsoft.AspNetCore.Mvc;
using SynologySlideshow.Api.Services;

namespace SynologySlideshow.Api.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly AdminSnapshotService _snapshotService;

    public AdminController(AdminSnapshotService snapshotService)
    {
        _snapshotService = snapshotService;
    }

    [HttpGet("snapshot")]
    public async Task<IActionResult> GetSnapshot() => Ok(await _snapshotService.BuildAsync());
}
