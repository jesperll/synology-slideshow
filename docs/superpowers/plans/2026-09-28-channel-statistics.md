# Channel Statistics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Track how many times each slide has been shown on each Channel, and let the admin see a Channel's total view count plus its most- and least-viewed slides.

**Architecture:** A new `SlideView` row (`ChannelId`, `SlideId`, `ViewCount`) accumulates per (channel, slide) pair, upserted by a `ViewStatsService`. `ChannelPlaybackService` calls it exactly where a slide actually becomes current — `AdvanceOneAsync` and `JumpOneAsync` (which also covers the timer-driven automatic advance, since it calls `AdvanceOneAsync` internally) — never on album switches alone, since those don't resolve to a shown slide by themselves. A REST endpoint exposes the numbers; the admin page gets an expandable "Stats" section per channel.

**Tech Stack:** No new packages.

**Spec:** `docs/superpowers/plans/2026-09-28-channel-feature-spec.md`

## Global Constraints

- Stats are tracked per Channel only, starting from the moment that Channel exists — there's no prior "anonymous" history to attribute (per the core-infrastructure and client-viewing plans, anonymous connections have no persisted identity at all).
- A view is counted on every resolved advance/jump, regardless of whether it lands on the same slide as before (no "was this actually different" check) and regardless of how long the previous slide was shown (no minimum-dwell filter).
- Counters are all-time and never automatically reset — only clearing a Channel (deleting it, per the core-infrastructure plan) clears its stats, via the existing cascade of that Channel's rows.

## Review Focus

- Switching a Channel's album must not, by itself, record a view — no slide is actually being shown yet at that instant (the index resets to none until the next advance or jump).
- The Channel's own automatic 30-second advance timer must record views exactly like a manual "next" request — most of a Channel's view history will come from the timer, not from someone manually clicking through.
- Revisiting the same slide (the album loops back around) must accumulate onto the same row, not create a second one for the same (channel, slide) pair.
- Requesting stats for a Channel that doesn't exist must return 404, not a 200 with an empty/zeroed body that looks like a real (if boring) channel.
- Stats must stay scoped per Channel — recording a view on one Channel must never bleed into another Channel's counts, even if both happen to be showing the exact same slide id from a shared album.

---

## Task 1: `SlideView` persistence and `ViewStatsService`

**Files:**
- Create: `SynologySlideshow.Api/Data/SlideView.cs`
- Modify: `SynologySlideshow.Api/Data/SlideshowDbContext.cs`
- Create: `SynologySlideshow.Api/Services/ViewStatsService.cs`
- Create: `SynologySlideshow.Api.Tests/Services/ViewStatsServiceTests.cs`
- Modify: `SynologySlideshow.Api/Program.cs`

**Interfaces:**
- Produces: `SynologySlideshow.Api.Data.SlideView { Id: int, ChannelId: int, SlideId: int, ViewCount: int }`; `SynologySlideshow.Api.Services.ViewStatsService` with `RecordViewAsync(int channelId, int slideId): Task` and `GetStatsAsync(int channelId): Task<ChannelStats>`; `SlideViewStat(int SlideId, int ViewCount)` and `ChannelStats(int TotalViews, SlideViewStat[] TopViewed, SlideViewStat[] LeastViewed)`. Task 2 hooks `RecordViewAsync` into playback and exposes `GetStatsAsync` over REST.

- [ ] **Step 1: Write the failing tests**

Create `SynologySlideshow.Api.Tests/Services/ViewStatsServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Services;
using Xunit;

namespace SynologySlideshow.Api.Tests.Services;

public class ViewStatsServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"stats-test-{Guid.NewGuid()}.db");
    private readonly ServiceProvider _provider;
    private readonly ViewStatsService _stats;

    public ViewStatsServiceTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<SlideshowDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        _provider = services.BuildServiceProvider();

        using (var scope = _provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<SlideshowDbContext>().Database.Migrate();
        }

        _stats = new ViewStatsService(_provider.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task FirstViewOfASlideStartsItsCountAtOne()
    {
        await _stats.RecordViewAsync(channelId: 1, slideId: 10);

        var stats = await _stats.GetStatsAsync(1);

        Assert.Equal(1, stats.TotalViews);
        Assert.Contains(stats.TopViewed, s => s.SlideId == 10 && s.ViewCount == 1);
    }

    [Fact]
    public async Task RepeatedViewsOfTheSameSlideAccumulate()
    {
        await _stats.RecordViewAsync(1, 10);
        await _stats.RecordViewAsync(1, 10);
        await _stats.RecordViewAsync(1, 10);

        var stats = await _stats.GetStatsAsync(1);

        Assert.Equal(3, stats.TotalViews);
        Assert.Contains(stats.TopViewed, s => s.SlideId == 10 && s.ViewCount == 3);
    }

    [Fact]
    public async Task StatsAreScopedPerChannel()
    {
        await _stats.RecordViewAsync(1, 10);
        await _stats.RecordViewAsync(2, 10);

        var statsChannel1 = await _stats.GetStatsAsync(1);

        Assert.Equal(1, statsChannel1.TotalViews);
    }

    [Fact]
    public async Task TopAndLeastViewedAreOrderedByCount()
    {
        await _stats.RecordViewAsync(1, 10);
        await _stats.RecordViewAsync(1, 10);
        await _stats.RecordViewAsync(1, 10);
        await _stats.RecordViewAsync(1, 20);

        var stats = await _stats.GetStatsAsync(1);

        Assert.Equal(10, stats.TopViewed.First().SlideId);
        Assert.Equal(20, stats.LeastViewed.First().SlideId);
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SynologySlideshow.Api.Tests --filter ViewStatsServiceTests`
Expected: build FAILS — `ViewStatsService` and `SlideView` don't exist yet.

- [ ] **Step 3: Implement the entity and service**

Create `SynologySlideshow.Api/Data/SlideView.cs`:

```csharp
namespace SynologySlideshow.Api.Data;

public class SlideView
{
    public int Id { get; set; }
    public int ChannelId { get; set; }
    public int SlideId { get; set; }
    public int ViewCount { get; set; }
}
```

Modify `SynologySlideshow.Api/Data/SlideshowDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace SynologySlideshow.Api.Data;

public class SlideshowDbContext : DbContext
{
    public SlideshowDbContext(DbContextOptions<SlideshowDbContext> options) : base(options)
    {
    }

    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<SlideView> SlideViews => Set<SlideView>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Channel>()
            .HasIndex(c => c.NormalizedName)
            .IsUnique();

        modelBuilder.Entity<SlideView>()
            .HasIndex(v => new { v.ChannelId, v.SlideId })
            .IsUnique();
    }
}
```

Create `SynologySlideshow.Api/Services/ViewStatsService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;

namespace SynologySlideshow.Api.Services;

public record SlideViewStat(int SlideId, int ViewCount);

public record ChannelStats(int TotalViews, SlideViewStat[] TopViewed, SlideViewStat[] LeastViewed);

public class ViewStatsService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ViewStatsService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RecordViewAsync(int channelId, int slideId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();

        var existing = await db.SlideViews.SingleOrDefaultAsync(v => v.ChannelId == channelId && v.SlideId == slideId);
        if (existing == null)
        {
            db.SlideViews.Add(new SlideView { ChannelId = channelId, SlideId = slideId, ViewCount = 1 });
        }
        else
        {
            existing.ViewCount++;
        }

        await db.SaveChangesAsync();
    }

    public async Task<ChannelStats> GetStatsAsync(int channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();

        var views = await db.SlideViews
            .Where(v => v.ChannelId == channelId)
            .ToListAsync();

        var totalViews = views.Sum(v => v.ViewCount);
        var topViewed = views.OrderByDescending(v => v.ViewCount).Take(5)
            .Select(v => new SlideViewStat(v.SlideId, v.ViewCount)).ToArray();
        var leastViewed = views.OrderBy(v => v.ViewCount).Take(5)
            .Select(v => new SlideViewStat(v.SlideId, v.ViewCount)).ToArray();

        return new ChannelStats(totalViews, topViewed, leastViewed);
    }
}
```

- [ ] **Step 4: Generate the migration**

Run:

```bash
dotnet ef migrations add AddSlideViews --project SynologySlideshow.Api --startup-project SynologySlideshow.Api
```

Expected: a new migration file appears in `SynologySlideshow.Api/Migrations/` adding the `SlideViews` table. Do not hand-edit it.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SynologySlideshow.Api.Tests --filter ViewStatsServiceTests`
Expected: PASS (4 tests).

- [ ] **Step 6: Register the service**

In `SynologySlideshow.Api/Program.cs`, add this line next to the other `AddSingleton` registrations:

```csharp
builder.Services.AddSingleton<ViewStatsService>();
```

- [ ] **Step 7: Run the full backend test suite**

Run: `dotnet test SynologySlideshow.Api.Tests`
Expected: PASS (every test across all plans so far).

- [ ] **Step 8: Commit**

```bash
git add SynologySlideshow.Api/Data/SlideView.cs SynologySlideshow.Api/Data/SlideshowDbContext.cs SynologySlideshow.Api/Services/ViewStatsService.cs SynologySlideshow.Api/Migrations SynologySlideshow.Api/Program.cs SynologySlideshow.Api.Tests/Services/ViewStatsServiceTests.cs
git commit -m "feat: add per-channel slide view tracking"
```

---

## Task 2: Hook view recording into playback, and expose it over REST

**Files:**
- Modify: `SynologySlideshow.Api/Services/ChannelPlaybackService.cs`
- Modify: `SynologySlideshow.Api/Controllers/ChannelsController.cs`
- Create: `SynologySlideshow.Api.Tests/Services/ChannelPlaybackServiceStatsTests.cs`
- Create: `SynologySlideshow.Api.Tests/Controllers/ChannelStatsControllerTests.cs`

**Interfaces:**
- Consumes: `ViewStatsService` (Task 1).
- Produces: `GET /api/channels/{id}/stats` → `ChannelStats`, or `404` if the channel doesn't exist — the exact contract Task 3 (frontend) consumes.

- [ ] **Step 1: Write the failing service-level tests**

Create `SynologySlideshow.Api.Tests/Services/ChannelPlaybackServiceStatsTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Services;
using SynologySlideshow.Api.Tests.TestDoubles;
using Xunit;

namespace SynologySlideshow.Api.Tests.Services;

public class ChannelPlaybackServiceStatsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"playback-stats-test-{Guid.NewGuid()}.db");
    private readonly ServiceProvider _provider;
    private readonly FakeHubContext _hub = new();
    private readonly FakeSlideSource _slides = new();
    private readonly ChannelPlaybackService _playback;
    private readonly ViewStatsService _viewStats;
    private readonly int _channelId;

    public ChannelPlaybackServiceStatsTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<SlideshowDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        _provider = services.BuildServiceProvider();

        using (var scope = _provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<SlideshowDbContext>().Database.Migrate();
        }

        _viewStats = new ViewStatsService(_provider.GetRequiredService<IServiceScopeFactory>());
        _playback = new ChannelPlaybackService(
            _slides,
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _hub,
            new SyncGroupService(),
            _viewStats);

        _slides.SlidesByAlbum[1] = new[] { new SlideRef(10), new SlideRef(20) };

        using var setupScope = _provider.CreateScope();
        var db = setupScope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = new Channel { Name = "kitchen", NormalizedName = "KITCHEN", CurrentAlbumId = 1, IsPaused = false };
        db.Channels.Add(channel);
        db.SaveChanges();
        _channelId = channel.Id;
    }

    [Fact]
    public async Task AdvancingRecordsAViewForTheResolvedSlide()
    {
        await _playback.AdvanceAsync(_channelId, 1); // -> slide 10

        var stats = await _viewStats.GetStatsAsync(_channelId);

        Assert.Equal(1, stats.TotalViews);
        Assert.Contains(stats.TopViewed, s => s.SlideId == 10 && s.ViewCount == 1);
    }

    [Fact]
    public async Task JumpingRecordsAViewForTheTargetSlide()
    {
        await _playback.JumpAsync(_channelId, 20);

        var stats = await _viewStats.GetStatsAsync(_channelId);

        Assert.Equal(1, stats.TotalViews);
        Assert.Contains(stats.TopViewed, s => s.SlideId == 20 && s.ViewCount == 1);
    }

    [Fact]
    public async Task RevisitingTheSameSlideAccumulatesItsCount()
    {
        await _playback.AdvanceAsync(_channelId, 1); // -> 10
        await _playback.AdvanceAsync(_channelId, 1); // -> 20
        await _playback.AdvanceAsync(_channelId, 1); // -> 10 again

        var stats = await _viewStats.GetStatsAsync(_channelId);

        Assert.Equal(3, stats.TotalViews);
        Assert.Contains(stats.TopViewed, s => s.SlideId == 10 && s.ViewCount == 2);
    }

    [Fact]
    public async Task SwitchingAlbumsRecordsNoViewUntilTheNextAdvanceOrJump()
    {
        await _playback.SetAlbumAsync(_channelId, 1);

        var stats = await _viewStats.GetStatsAsync(_channelId);

        Assert.Equal(0, stats.TotalViews);
    }

    [Fact]
    public async Task TheAutomaticAdvanceTimerRecordsAViewJustLikeAManualRequest()
    {
        await _playback.TickAsync(_channelId); // simulates the 30-second timer firing on its own

        var stats = await _viewStats.GetStatsAsync(_channelId);

        Assert.Equal(1, stats.TotalViews);
        Assert.Contains(stats.TopViewed, s => s.SlideId == 10 && s.ViewCount == 1);
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SynologySlideshow.Api.Tests --filter ChannelPlaybackServiceStatsTests`
Expected: build FAILS — `ChannelPlaybackService`'s constructor doesn't take a `ViewStatsService` yet.

- [ ] **Step 3: Hook recording into `AdvanceOneAsync` and `JumpOneAsync`**

In `SynologySlideshow.Api/Services/ChannelPlaybackService.cs`, add the field and constructor parameter:

```csharp
    private readonly SyncGroupService _syncGroups;
    private readonly ViewStatsService _viewStats;
    private readonly ConcurrentDictionary<int, ChannelRuntimeState> _runtime = new();
    private readonly ConcurrentDictionary<int, Timer> _timers = new();

    public ChannelPlaybackService(
        ISlideSource slideSource,
        IServiceScopeFactory scopeFactory,
        IHubContext<SlideshowHub> hubContext,
        SyncGroupService syncGroups,
        ViewStatsService viewStats)
    {
        _slideSource = slideSource;
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _syncGroups = syncGroups;
        _viewStats = viewStats;
    }
```

Then change `AdvanceOneAsync` and `JumpOneAsync` to record a view once the broadcast dto resolves a slide:

```csharp
    private async Task<ChannelStateDto> AdvanceOneAsync(int channelId, int offset)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        var runtime = GetOrCreateRuntime(channelId);
        var slides = GetSlides(channel.CurrentAlbumId);
        if (slides.Length > 0)
        {
            var current = runtime.CurrentSlideIndex ?? -1;
            runtime.CurrentSlideIndex = ((current + offset) % slides.Length + slides.Length) % slides.Length;
        }

        var dto = await BuildAndBroadcastAsync(channel, runtime);
        if (dto.CurrentSlideId is int slideId)
        {
            await _viewStats.RecordViewAsync(channelId, slideId);
        }
        return dto;
    }

    private async Task<ChannelStateDto> JumpOneAsync(int channelId, int slideId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        var runtime = GetOrCreateRuntime(channelId);
        var slides = GetSlides(channel.CurrentAlbumId);
        var index = Array.FindIndex(slides, s => s.Id == slideId);
        if (index >= 0)
        {
            runtime.CurrentSlideIndex = index;
        }

        var dto = await BuildAndBroadcastAsync(channel, runtime);
        if (dto.CurrentSlideId is int resolvedSlideId)
        {
            await _viewStats.RecordViewAsync(channelId, resolvedSlideId);
        }
        return dto;
    }
```

(Every other method — `GetStateAsync`, `TogglePauseAsync`/`SetPausedOneAsync`, `SetAlbumOneAsync`, `LinkAsync`/`UnlinkAsync`, `TickAsync`, `StartTimer`/`StopTimer`, `ApplyToGroupAsync`, `GetOrCreateRuntime`, `GetSlides`, `BuildAndBroadcastAsync`, `ToDto` — is unchanged from the sync-linking plan.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SynologySlideshow.Api.Tests --filter ChannelPlaybackServiceStatsTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Write the failing REST endpoint tests**

Create `SynologySlideshow.Api.Tests/Controllers/ChannelStatsControllerTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SynologySlideshow.Api.Controllers;
using SynologySlideshow.Api.Services;
using Xunit;

namespace SynologySlideshow.Api.Tests.Controllers;

public class ChannelStatsControllerTests : IClassFixture<SlideshowApiFactory>
{
    private readonly SlideshowApiFactory _factory;
    private readonly HttpClient _client;

    public ChannelStatsControllerTests(SlideshowApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ReturnsRecordedViewsForTheChannel()
    {
        var created = await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = "stats-endpoint-test" });
        var channel = await created.Content.ReadFromJsonAsync<ChannelSummary>();

        using (var scope = _factory.Services.CreateScope())
        {
            var viewStats = scope.ServiceProvider.GetRequiredService<ViewStatsService>();
            await viewStats.RecordViewAsync(channel!.Id, 10);
            await viewStats.RecordViewAsync(channel.Id, 10);
            await viewStats.RecordViewAsync(channel.Id, 20);
        }

        var stats = await _client.GetFromJsonAsync<ChannelStats>($"/api/channels/{channel!.Id}/stats");

        Assert.Equal(3, stats!.TotalViews);
        Assert.Contains(stats.TopViewed, s => s.SlideId == 10 && s.ViewCount == 2);
    }

    [Fact]
    public async Task ReturnsNotFoundForAnUnknownChannel()
    {
        var response = await _client.GetAsync("/api/channels/999999/stats");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] **Step 6: Run the tests to verify they fail**

Run: `dotnet test SynologySlideshow.Api.Tests --filter ChannelStatsControllerTests`
Expected: build FAILS — `ChannelsController` has no `/stats` route yet.

- [ ] **Step 7: Add the endpoint**

Modify `SynologySlideshow.Api/Controllers/ChannelsController.cs`: add a `ViewStatsService` dependency and the new route, on top of the `IHubContext<SlideshowHub>`/`AdminSnapshotService` presence-broadcast wiring the admin-interface plan already added.

```csharp
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
    private static readonly string[] ReservedNames = { "ADMIN" };

    private readonly SlideshowDbContext _db;
    private readonly ChannelPlaybackService _playback;
    private readonly IHubContext<SlideshowHub> _hubContext;
    private readonly AdminSnapshotService _snapshotService;
    private readonly ViewStatsService _viewStats;

    public ChannelsController(
        SlideshowDbContext db,
        ChannelPlaybackService playback,
        IHubContext<SlideshowHub> hubContext,
        AdminSnapshotService snapshotService,
        ViewStatsService viewStats)
    {
        _db = db;
        _playback = playback;
        _hubContext = hubContext;
        _snapshotService = snapshotService;
        _viewStats = viewStats;
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
        await BroadcastPresenceAsync();
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
        await BroadcastPresenceAsync();
        return NoContent();
    }

    [HttpGet("{id}/stats")]
    public async Task<IActionResult> GetStats(int id)
    {
        if (!await _db.Channels.AnyAsync(c => c.Id == id)) return NotFound();
        return Ok(await _viewStats.GetStatsAsync(id));
    }

    private async Task BroadcastPresenceAsync()
    {
        var snapshot = await _snapshotService.BuildAsync();
        await _hubContext.Clients.Group(SlideshowHub.AdminGroupName).SendAsync("PresenceChanged", snapshot);
    }
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test SynologySlideshow.Api.Tests --filter ChannelStatsControllerTests`
Expected: PASS (2 tests).

- [ ] **Step 9: Run the full backend test suite**

Run: `dotnet test SynologySlideshow.Api.Tests`
Expected: PASS (every test across all five plans).

- [ ] **Step 10: Commit**

```bash
git add SynologySlideshow.Api/Services/ChannelPlaybackService.cs SynologySlideshow.Api/Controllers/ChannelsController.cs SynologySlideshow.Api.Tests/Services/ChannelPlaybackServiceStatsTests.cs SynologySlideshow.Api.Tests/Controllers/ChannelStatsControllerTests.cs
git commit -m "feat: record slide views during playback and expose channel stats"
```

---

## Task 3: Show stats in the admin page

**Files:**
- Modify: `SynologySlideshow.Web/src/types/index.ts`
- Modify: `SynologySlideshow.Web/src/services/api.ts`
- Modify: `SynologySlideshow.Web/src/components/AdminPage.tsx`
- Modify: `SynologySlideshow.Web/src/components/AdminPage.test.tsx`

**Interfaces:**
- Consumes: `GET /api/channels/{id}/stats` (Task 2).
- Produces: `getChannelStats(channelId: number)` in `services/api.ts`; an expandable "Stats" section per channel row in `AdminPage`.

- [ ] **Step 1: Write the failing test**

Add this test to `SynologySlideshow.Web/src/components/AdminPage.test.tsx`, inside the existing `describe('AdminPage', ...)` block:

```tsx
  it('shows total views and top/least viewed slides when Stats is expanded', async () => {
    mockHook();
    vi.mocked(api.getChannelStats).mockResolvedValue({
      data: { totalViews: 5, topViewed: [{ slideId: 10, viewCount: 3 }], leastViewed: [{ slideId: 20, viewCount: 2 }] }
    } as any);

    render(<AdminPage />);

    fireEvent.click(screen.getAllByRole('button', { name: 'Stats' })[0]);

    expect(await screen.findByText('Total views: 5')).toBeInTheDocument();
    expect(screen.getByText('Slide #10 — 3 views')).toBeInTheDocument();
    expect(screen.getByText('Slide #20 — 2 views')).toBeInTheDocument();
  });
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd SynologySlideshow.Web && npm test -- AdminPage`
Expected: FAIL — there's no "Stats" button yet, and `api.getChannelStats` doesn't exist.

- [ ] **Step 3: Add the types and API call**

Add to `SynologySlideshow.Web/src/types/index.ts`:

```ts
export interface SlideViewStat {
  slideId: number;
  viewCount: number;
}

export interface ChannelStats {
  totalViews: number;
  topViewed: SlideViewStat[];
  leastViewed: SlideViewStat[];
}
```

Add to `SynologySlideshow.Web/src/services/api.ts` (alongside the other exports):

```ts
export const getChannelStats = (channelId: number) => api.get<ChannelStats>(`/channels/${channelId}/stats`);
```

- [ ] **Step 4: Add the stats UI to `AdminPage`**

In `SynologySlideshow.Web/src/components/AdminPage.tsx`, add the import:

```tsx
import { createChannel, deleteChannel, getAlbums, getAlbumSlides, getChannelStats } from '../services/api';
import { Album, ChannelStats, Slide } from '../types';
```

Add state, alongside the existing `useState` calls:

```tsx
  const [statsByChannel, setStatsByChannel] = useState<Record<number, ChannelStats>>({});
  const [statsExpandedId, setStatsExpandedId] = useState<number | null>(null);
```

Add the toggle handler, alongside `toggleExpanded`:

```tsx
  const toggleStats = async (channelId: number) => {
    if (statsExpandedId === channelId) {
      setStatsExpandedId(null);
      return;
    }
    if (!statsByChannel[channelId]) {
      const response = await getChannelStats(channelId);
      setStatsByChannel((current) => ({ ...current, [channelId]: response.data }));
    }
    setStatsExpandedId(channelId);
  };
```

Add a "Stats" button to the Controls cell, alongside the existing buttons there:

```tsx
                  <button onClick={() => toggleStats(channel.channelId)}>
                    {statsExpandedId === channel.channelId ? 'Hide stats' : 'Stats'}
                  </button>
```

Add the expandable stats row, immediately after the existing "Jump to slide" expandable row (the one gated on `expandedChannelId`):

```tsx
              {statsExpandedId === channel.channelId && statsByChannel[channel.channelId] && (
                <tr>
                  <td colSpan={8}>
                    <div className="admin-stats">
                      <p>Total views: {statsByChannel[channel.channelId].totalViews}</p>
                      <div>
                        <strong>Most viewed:</strong>
                        <ul>
                          {statsByChannel[channel.channelId].topViewed.map((s) => (
                            <li key={s.slideId}>
                              Slide #{s.slideId} — {s.viewCount} views
                            </li>
                          ))}
                        </ul>
                      </div>
                      <div>
                        <strong>Least viewed:</strong>
                        <ul>
                          {statsByChannel[channel.channelId].leastViewed.map((s) => (
                            <li key={s.slideId}>
                              Slide #{s.slideId} — {s.viewCount} views
                            </li>
                          ))}
                        </ul>
                      </div>
                    </div>
                  </td>
                </tr>
              )}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd SynologySlideshow.Web && npm test -- AdminPage`
Expected: PASS (10 tests).

- [ ] **Step 6: Run the full frontend test suite and the production build**

Run: `cd SynologySlideshow.Web && npm test && npm run build`
Expected: all tests PASS; build succeeds.

- [ ] **Step 7: Commit**

```bash
git add SynologySlideshow.Web/src/types/index.ts SynologySlideshow.Web/src/services/api.ts SynologySlideshow.Web/src/components/AdminPage.tsx SynologySlideshow.Web/src/components/AdminPage.test.tsx
git commit -m "feat: show per-channel view statistics in the admin page"
```
