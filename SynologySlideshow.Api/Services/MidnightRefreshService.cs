namespace SynologySlideshow.Api.Services;

// Re-fetches the photo library from Synology once a day at midnight, so newly-added or
// removed photos show up without needing a restart. "Midnight" is the container's local time
// (DateTime.Now) - set the TZ environment variable if that isn't already the timezone you want.
public class MidnightRefreshService : BackgroundService
{
    private readonly SlideShowService _slideShowService;
    private readonly ILogger<MidnightRefreshService> _logger;

    public MidnightRefreshService(SlideShowService slideShowService, ILogger<MidnightRefreshService> logger)
    {
        _slideShowService = slideShowService;
        _logger = logger;
    }

    public static TimeSpan GetDelayUntilNextMidnight(DateTime now) => now.Date.AddDays(1) - now;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextMidnight(DateTime.Now);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await _slideShowService.RefreshAsync();
                _logger.LogInformation("Refreshed the photo library at midnight.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Midnight photo library refresh failed.");
            }
        }
    }
}
