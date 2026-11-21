using SynologySlideshow.Api.Services;
using Xunit;

namespace SynologySlideshow.Api.Tests.Services;

public class MidnightRefreshServiceTests
{
    [Fact]
    public void JustAfterMidnightDelaysAlmostTwentyFourHours()
    {
        var now = new DateTime(2026, 3, 5, 0, 0, 1);

        var delay = MidnightRefreshService.GetDelayUntilNextMidnight(now);

        Assert.Equal(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1), delay);
    }

    [Fact]
    public void JustBeforeMidnightDelaysAlmostNoTime()
    {
        var now = new DateTime(2026, 3, 5, 23, 59, 59);

        var delay = MidnightRefreshService.GetDelayUntilNextMidnight(now);

        Assert.Equal(TimeSpan.FromSeconds(1), delay);
    }

    [Fact]
    public void AtExactlyMidnightDelaysAFullDay()
    {
        var now = new DateTime(2026, 3, 5, 0, 0, 0);

        var delay = MidnightRefreshService.GetDelayUntilNextMidnight(now);

        Assert.Equal(TimeSpan.FromHours(24), delay);
    }

    [Fact]
    public void MidDayDelaysToTheFollowingMidnight()
    {
        var now = new DateTime(2026, 3, 5, 14, 30, 0);

        var delay = MidnightRefreshService.GetDelayUntilNextMidnight(now);

        Assert.Equal(new DateTime(2026, 3, 6, 0, 0, 0) - now, delay);
    }
}
