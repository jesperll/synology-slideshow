# Channel Core Infrastructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a persisted `Channel` entity with server-authoritative current-album/current-slide/paused state, backed by SQLite, and a SignalR hub that lets any number of browsers join a channel by name and receive live pushes whenever its state changes.

**Architecture:** `SynologySlideshow.Api` gains a `Data/` folder (EF Core `SlideshowDbContext` + `Channel` entity), a `Services/ChannelPlaybackService.cs` (the single owner of both persisted and in-memory-per-process channel state, including the server-side advance timer), and a `Realtime/` folder (`SlideshowHub` + the `ChannelStateDto` broadcast/return payload). `SynologySlideshow.Core` is untouched — this feature is entirely an API-layer concern. A new `SynologySlideshow.Api.Tests` project is added since none exists today.

**Tech Stack:** ASP.NET Core SignalR (built into the Web SDK, no new package), EF Core 10 + SQLite, xUnit + `Microsoft.AspNetCore.Mvc.Testing` + `Microsoft.AspNetCore.SignalR.Client` for tests.

**Spec:** `docs/superpowers/plans/2026-09-28-channel-feature-spec.md`

## Global Constraints

- Only this API creates/deletes Channels — no client-facing "create" path exists anywhere in this plan or any later one.
- Channel names are unique, compared case-insensitively after trimming (a human-facing name and a URL segment both need this).
- There is no server-side "Client" entity anywhere — only `Channel` is persisted.
- No authentication on any endpoint — matches the rest of the app's LAN-trust model (see README: no 2FA).
- The advance timer is server-owned and runs continuously for every existing, unpaused Channel regardless of how many viewers are currently attached (a Channel's transport state is independent of who's in the room, same as a real audio zone) — this plan's own design decision, not dictated by the spec, documented here so later plans don't second-guess it.

## Review Focus

- Creating a channel whose name differs from an existing one only by case or surrounding whitespace ("Kitchen " vs "kitchen") must still be rejected — a reasonable person expects "unique name" to mean that, not byte-for-byte distinct strings.
- Issuing a control request (advance/jump/toggle-pause/switch-album) against a channel ID that was just deleted, or never existed, must fail that one request cleanly — it must not crash the SignalR connection or take down other in-flight requests.
- Switching a channel to an album that doesn't exist (or exists but has zero slides) must resolve to "no current slide" rather than throwing or silently keeping a stale slide from the previous album.
- Restarting the process must resume each channel exactly as it was left — a channel persisted as paused must still be paused (and its timer must not silently advance it) after the process restarts.
- Two callers creating a channel with the same name at effectively the same instant must not both succeed — the database's unique constraint, not just an in-memory check, must be what ultimately prevents it.
- A channel named `admin` must be rejected at creation — Plan 3 maps the admin UI to the single-segment route `/admin`, and the client-side routing this feature relies on (Plan 2) treats any other single segment as a channel name; letting a channel claim that name would make `/admin` unreachable.

---

## Task 1: SQLite persistence for Channels

**Files:**
- Create: `SynologySlideshow.Api/Data/Channel.cs`
- Create: `SynologySlideshow.Api/Data/SlideshowDbContext.cs`
- Create: `SynologySlideshow.Api.Tests/SynologySlideshow.Api.Tests.csproj`
- Create: `SynologySlideshow.Api.Tests/Data/SlideshowDbContextTests.cs`
- Modify: `SynologySlideshow.Api/SynologySlideshow.Api.csproj`
- Modify: `SynologySlideshow.Api/Program.cs`
- Modify: `SynologySlideshow.Api/appsettings.json`
- Modify: `SynologySlideshow.sln`
- Modify: `docker-compose.yml`
- Modify: `docker-compose.server.yml`
- Modify: `.gitignore`

**Interfaces:**
- Produces: `SynologySlideshow.Api.Data.Channel` (`Id: int`, `Name: string`, `NormalizedName: string`, `CurrentAlbumId: int?`, `IsPaused: bool`, `CreatedAt: DateTime`), `SynologySlideshow.Api.Data.SlideshowDbContext` (`DbSet<Channel> Channels`), and a public `Program` class other test projects can target with `WebApplicationFactory<Program>`.

- [ ] **Step 1: Create the test project and wire it into the solution**

```bash
dotnet new xunit -n SynologySlideshow.Api.Tests -o SynologySlideshow.Api.Tests
dotnet sln SynologySlideshow.sln add SynologySlideshow.Api.Tests/SynologySlideshow.Api.Tests.csproj
dotnet add SynologySlideshow.Api.Tests/SynologySlideshow.Api.Tests.csproj reference SynologySlideshow.Api/SynologySlideshow.Api.csproj
```

Then open `SynologySlideshow.Api.Tests/SynologySlideshow.Api.Tests.csproj` and replace its contents so the `TargetFramework` matches the rest of the solution and the packages this plan needs are present from the start:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\SynologySlideshow.Api\SynologySlideshow.Api.csproj" />
  </ItemGroup>

</Project>
```

(Package versions above are current as of this plan's writing — bump to the latest matching `10.x` patch if `dotnet restore` reports a newer one available.)

- [ ] **Step 2: Write the failing test**

Create `SynologySlideshow.Api.Tests/Data/SlideshowDbContextTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;
using Xunit;

namespace SynologySlideshow.Api.Tests.Data;

public class SlideshowDbContextTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"slideshow-test-{Guid.NewGuid()}.db");
    private readonly SlideshowDbContext _context;

    public SlideshowDbContextTests()
    {
        var options = new DbContextOptionsBuilder<SlideshowDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _context = new SlideshowDbContext(options);
        _context.Database.Migrate();
    }

    [Fact]
    public async Task SavesAndRetrievesChannel()
    {
        _context.Channels.Add(new Channel { Name = "kitchen", NormalizedName = "KITCHEN" });
        await _context.SaveChangesAsync();

        var saved = await _context.Channels.SingleAsync(c => c.Name == "kitchen");

        Assert.Equal("kitchen", saved.Name);
        Assert.True(saved.IsPaused);
        Assert.Null(saved.CurrentAlbumId);
    }

    [Fact]
    public async Task RejectsDuplicateNormalizedName()
    {
        _context.Channels.Add(new Channel { Name = "kitchen", NormalizedName = "KITCHEN" });
        await _context.SaveChangesAsync();

        _context.Channels.Add(new Channel { Name = "Kitchen ", NormalizedName = "KITCHEN" });

        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test SynologySlideshow.Api.Tests --filter SlideshowDbContextTests`
Expected: build FAILS — `Channel` and `SlideshowDbContext` don't exist yet.

- [ ] **Step 4: Add the EF Core packages and implement the entity + context**

Add to `SynologySlideshow.Api/SynologySlideshow.Api.csproj`'s existing `<ItemGroup>` (alongside the `ProjectReference`):

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

Create `SynologySlideshow.Api/Data/Channel.cs`:

```csharp
namespace SynologySlideshow.Api.Data;

public class Channel
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string NormalizedName { get; set; } = null!;
    public int? CurrentAlbumId { get; set; }
    public bool IsPaused { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

Create `SynologySlideshow.Api/Data/SlideshowDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace SynologySlideshow.Api.Data;

public class SlideshowDbContext : DbContext
{
    public SlideshowDbContext(DbContextOptions<SlideshowDbContext> options) : base(options)
    {
    }

    public DbSet<Channel> Channels => Set<Channel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Channel>()
            .HasIndex(c => c.NormalizedName)
            .IsUnique();
    }
}
```

- [ ] **Step 5: Wire the context into `Program.cs` and add the connection string**

In `SynologySlideshow.Api/appsettings.json`, add a `ConnectionStrings` section so the final file reads:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "Slideshow": "Data Source=data/slideshow.db"
  }
}
```

In `SynologySlideshow.Api/Program.cs`, add the using directives and DB registration, run migrations right after the app is built, and mark the top-level `Program` class public so `WebApplicationFactory<Program>` can target it from the test project. The full file should read:

```csharp
using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add HttpClient
builder.Services.AddHttpClient();

// Add Controllers
builder.Services.AddControllers();

// Add CORS for React app (development and production)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:5174") // Vite dev server
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configuration
builder.Configuration
    .AddEnvironmentVariables()
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true, true);

// Configure Synology options
builder.Services.Configure<SynologyOptions>(
    options => builder.Configuration.GetSection("Synology").Bind(options));

// Add SlideShow service
builder.Services.AddSingleton<SlideShowService>();

// Channel persistence
Directory.CreateDirectory("data");
builder.Services.AddDbContext<SlideshowDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Slideshow") ?? "Data Source=data/slideshow.db"));

var app = builder.Build();

using (var migrationScope = app.Services.CreateScope())
{
    migrationScope.ServiceProvider.GetRequiredService<SlideshowDbContext>().Database.Migrate();
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Serve static files from wwwroot (React build)
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors(); // Enable CORS

app.UseRouting();

app.MapControllers();

// Fallback to index.html for client-side routing
app.MapFallbackToFile("index.html");

// Initialize SlideShow service
await app.Services.GetRequiredService<SlideShowService>().InitAsync();

app.Run();

public partial class Program { }
```

- [ ] **Step 6: Generate the initial migration**

Run (installing the tool first if `dotnet ef` isn't already available):

```bash
dotnet tool install --global dotnet-ef --version 10.0.0 || true
dotnet ef migrations add InitialCreate --project SynologySlideshow.Api --startup-project SynologySlideshow.Api
```

Expected: a new `SynologySlideshow.Api/Migrations/` folder appears containing `<timestamp>_InitialCreate.cs`, `<timestamp>_InitialCreate.Designer.cs`, and `SlideshowDbContextModelSnapshot.cs`. Do not hand-edit these generated files.

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test SynologySlideshow.Api.Tests --filter SlideshowDbContextTests`
Expected: PASS (2 tests).

- [ ] **Step 8: Add the Docker volume and ignore the local database file**

In both `docker-compose.yml` and `docker-compose.server.yml`, add a volume for the data directory next to the existing `volumes:`/`ports:` entries (in `docker-compose.yml`, which has no `volumes:` key yet, add one):

```yaml
    volumes:
      - ./data:/app/data
```

(In `docker-compose.server.yml`, add `- ./data:/app/data` as an additional line under the existing `volumes:` key, alongside `./logs:/app/logs`.)

Add to `.gitignore`:

```
data/
```

- [ ] **Step 9: Commit**

```bash
git add SynologySlideshow.Api/Data SynologySlideshow.Api/Migrations SynologySlideshow.Api/Program.cs SynologySlideshow.Api/appsettings.json SynologySlideshow.Api/SynologySlideshow.Api.csproj SynologySlideshow.Api.Tests SynologySlideshow.sln docker-compose.yml docker-compose.server.yml .gitignore
git commit -m "feat: add SQLite-backed Channel entity and persistence"
```

---

## Task 2: Channel create/list/delete REST API

**Files:**
- Create: `SynologySlideshow.Api/Controllers/ChannelsController.cs`
- Create: `SynologySlideshow.Api/Controllers/ChannelDtos.cs`
- Create: `SynologySlideshow.Api.Tests/SlideshowApiFactory.cs`
- Create: `SynologySlideshow.Api.Tests/TestDoubles/TestSlideShowService.cs`
- Create: `SynologySlideshow.Api.Tests/Controllers/ChannelsControllerTests.cs`
- Modify: `SynologySlideshow.Api/Services/SlideShowService.cs`

**Interfaces:**
- Consumes: `SlideshowDbContext` (Task 1).
- Produces: `GET /api/channels` → `ChannelSummary[]`; `POST /api/channels` (body `CreateChannelRequest { Name: string }`) → `201` + `ChannelSummary`, or `409` on a duplicate name; `DELETE /api/channels/{id}` → `204`, or `404`. `SynologySlideshow.Api.Controllers.ChannelSummary { Id: int, Name: string }`. `SynologySlideshow.Api.Tests.SlideshowApiFactory : WebApplicationFactory<Program>` — the shared test host used by every later hub/controller test in this and later plans, exposing `DbPath: string`.

Every `WebApplicationFactory<Program>`-based test needs the app to boot without making a real network call to a Synology NAS — `Program.cs` unconditionally awaits `SlideShowService.InitAsync()`, which logs in over HTTP. This task adds the one seam needed to swap that out in tests.

- [ ] **Step 1: Write the failing tests**

Create `SynologySlideshow.Api.Tests/TestDoubles/TestSlideShowService.cs`:

```csharp
using Microsoft.Extensions.Options;
using SynologySlideshow.Api.Services;

namespace SynologySlideshow.Api.Tests.TestDoubles;

public class TestSlideShowService : SlideShowService
{
    public TestSlideShowService() : base(Options.Create(new SynologyOptions
    {
        Uri = "https://example.invalid/webapi",
        Username = "test",
        Password = "test"
    }))
    {
    }

    public override Task InitAsync() => Task.CompletedTask;
}
```

Create `SynologySlideshow.Api.Tests/SlideshowApiFactory.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SynologySlideshow.Api.Services;
using SynologySlideshow.Api.Tests.TestDoubles;

namespace SynologySlideshow.Api.Tests;

public class SlideshowApiFactory : WebApplicationFactory<Program>
{
    public readonly string DbPath = Path.Combine(Path.GetTempPath(), $"slideshow-api-test-{Guid.NewGuid()}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Slideshow"] = $"Data Source={DbPath}"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<SlideShowService>();
            services.AddSingleton<SlideShowService, TestSlideShowService>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(DbPath)) File.Delete(DbPath);
    }
}
```

Create `SynologySlideshow.Api.Tests/Controllers/ChannelsControllerTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using SynologySlideshow.Api.Controllers;
using Xunit;

namespace SynologySlideshow.Api.Tests.Controllers;

public class ChannelsControllerTests : IClassFixture<SlideshowApiFactory>
{
    private readonly HttpClient _client;

    public ChannelsControllerTests(SlideshowApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateThenListReturnsTheNewChannel()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = "kitchen" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var listResponse = await _client.GetFromJsonAsync<List<ChannelSummary>>("/api/channels");
        Assert.Contains(listResponse!, c => c.Name == "kitchen");
    }

    [Fact]
    public async Task CreateWithDuplicateNameReturnsConflict()
    {
        await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = "living-room" });
        var response = await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = "living-room" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateWithDifferentCasingOfExistingNameReturnsConflict()
    {
        await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = "Office" });
        var response = await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = " office " });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("Admin")]
    [InlineData(" ADMIN ")]
    public async Task CreateWithReservedNameReturnsBadRequest(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRemovesTheChannel()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = "bedroom" });
        var created = await createResponse.Content.ReadFromJsonAsync<ChannelSummary>();

        var deleteResponse = await _client.DeleteAsync($"/api/channels/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await _client.GetFromJsonAsync<List<ChannelSummary>>("/api/channels");
        Assert.DoesNotContain(listResponse!, c => c.Id == created.Id);
    }

    [Fact]
    public async Task DeleteOfUnknownIdReturnsNotFound()
    {
        var response = await _client.DeleteAsync("/api/channels/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SynologySlideshow.Api.Tests --filter ChannelsControllerTests`
Expected: build FAILS — `ChannelsController`, `ChannelSummary`, `CreateChannelRequest` don't exist yet, and `SlideShowService.InitAsync` isn't overridable yet.

- [ ] **Step 3: Make `InitAsync` overridable**

In `SynologySlideshow.Api/Services/SlideShowService.cs`, change:

```csharp
public async Task InitAsync()
```

to:

```csharp
public virtual async Task InitAsync()
```

- [ ] **Step 4: Implement the controller and DTOs**

Create `SynologySlideshow.Api/Controllers/ChannelDtos.cs`:

```csharp
namespace SynologySlideshow.Api.Controllers;

public class ChannelSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
}

public class CreateChannelRequest
{
    public string Name { get; set; } = null!;
}
```

Create `SynologySlideshow.Api/Controllers/ChannelsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;

namespace SynologySlideshow.Api.Controllers;

[ApiController]
[Route("api/channels")]
public class ChannelsController : ControllerBase
{
    private static readonly string[] ReservedNames = { "ADMIN" };

    private readonly SlideshowDbContext _db;

    public ChannelsController(SlideshowDbContext db)
    {
        _db = db;
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

        return CreatedAtAction(nameof(List), new ChannelSummary { Id = channel.Id, Name = channel.Name });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var channel = await _db.Channels.FindAsync(id);
        if (channel == null) return NotFound();

        _db.Channels.Remove(channel);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SynologySlideshow.Api.Tests --filter ChannelsControllerTests`
Expected: PASS (8 tests).

- [ ] **Step 6: Commit**

```bash
git add SynologySlideshow.Api/Controllers/ChannelDtos.cs SynologySlideshow.Api/Controllers/ChannelsController.cs SynologySlideshow.Api/Services/SlideShowService.cs SynologySlideshow.Api.Tests
git commit -m "feat: add channel create/list/delete REST API"
```

---

## Task 3: Server-owned playback state and advance timer

**Files:**
- Create: `SynologySlideshow.Api/Services/ISlideSource.cs`
- Create: `SynologySlideshow.Api/Services/SlideShowSlideSource.cs`
- Create: `SynologySlideshow.Api/Realtime/ChannelStateDto.cs`
- Create: `SynologySlideshow.Api/Services/ChannelPlaybackService.cs`
- Create: `SynologySlideshow.Api/Services/ChannelTimerStartup.cs`
- Create: `SynologySlideshow.Api.Tests/TestDoubles/FakeSlideSource.cs`
- Create: `SynologySlideshow.Api.Tests/TestDoubles/FakeHubContext.cs`
- Create: `SynologySlideshow.Api.Tests/Services/ChannelPlaybackServiceTests.cs`
- Modify: `SynologySlideshow.Api/Controllers/ChannelsController.cs`
- Modify: `SynologySlideshow.Api/Program.cs`

**Interfaces:**
- Consumes: `SlideshowDbContext`, `Channel` (Task 1); `ChannelSummary`, `CreateChannelRequest` (Task 2). `SlideShowService.SlideShow.GetSlides(int albumId): PhotoSlide[]` (existing).
- Produces: `SynologySlideshow.Api.Services.ISlideSource.GetSlides(int albumId): SlideRef[]` and the trivial record `SlideRef(int Id)`; `SynologySlideshow.Api.Realtime.ChannelStateDto { ChannelId: int, Name: string, CurrentAlbumId: int?, CurrentSlideId: int?, IsPaused: bool }`; `SynologySlideshow.Api.Services.ChannelPlaybackService` with `Task<ChannelStateDto> GetStateAsync(int channelId)`, `Task<ChannelStateDto> AdvanceAsync(int channelId, int offset)`, `Task<ChannelStateDto> JumpAsync(int channelId, int slideId)`, `Task<ChannelStateDto> TogglePauseAsync(int channelId)`, `Task<ChannelStateDto> SetAlbumAsync(int channelId, int albumId)`, `Task TickAsync(int channelId)`, `void StartTimer(int channelId)`, `void StopTimer(int channelId)`. This is the one place later plans (sync-linking, statistics) hook in.

`SlideshowHub` doesn't exist until Task 4, but `ChannelPlaybackService` needs to depend on `IHubContext<SlideshowHub>` to broadcast. Declare a minimal placeholder `Hub` subclass now so this compiles; Task 4 fills it in with real methods (the placeholder's group-naming helper is what Task 4 keeps).

- [ ] **Step 1: Write the failing tests**

Create `SynologySlideshow.Api.Tests/TestDoubles/FakeSlideSource.cs`:

```csharp
using SynologySlideshow.Api.Services;

namespace SynologySlideshow.Api.Tests.TestDoubles;

public class FakeSlideSource : ISlideSource
{
    public Dictionary<int, SlideRef[]> SlidesByAlbum { get; } = new();

    public SlideRef[] GetSlides(int albumId) =>
        SlidesByAlbum.TryGetValue(albumId, out var slides) ? slides : Array.Empty<SlideRef>();
}
```

Create `SynologySlideshow.Api.Tests/TestDoubles/FakeHubContext.cs`:

```csharp
using Microsoft.AspNetCore.SignalR;
using SynologySlideshow.Api.Realtime;

namespace SynologySlideshow.Api.Tests.TestDoubles;

public class FakeHubContext : IHubContext<SlideshowHub>
{
    public List<(string Group, string Method, object?[] Args)> Sent { get; } = new();

    public IHubClients Clients { get; }
    public IGroupManager Groups => throw new NotSupportedException("Not used by ChannelPlaybackService.");

    public FakeHubContext()
    {
        Clients = new FakeHubClients(this);
    }

    private class FakeHubClients : IHubClients
    {
        private readonly FakeHubContext _owner;
        public FakeHubClients(FakeHubContext owner) => _owner = owner;

        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => new FakeClientProxy(_owner, groupName);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private class FakeClientProxy : IClientProxy
    {
        private readonly FakeHubContext _owner;
        private readonly string _group;
        public FakeClientProxy(FakeHubContext owner, string group)
        {
            _owner = owner;
            _group = group;
        }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            _owner.Sent.Add((_group, method, args));
            return Task.CompletedTask;
        }
    }
}
```

Create `SynologySlideshow.Api.Tests/Services/ChannelPlaybackServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Realtime;
using SynologySlideshow.Api.Services;
using SynologySlideshow.Api.Tests.TestDoubles;
using Xunit;

namespace SynologySlideshow.Api.Tests.Services;

public class ChannelPlaybackServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"playback-test-{Guid.NewGuid()}.db");
    private readonly ServiceProvider _provider;
    private readonly FakeHubContext _hub = new();
    private readonly FakeSlideSource _slides = new();
    private readonly ChannelPlaybackService _playback;
    private readonly int _channelId;

    public ChannelPlaybackServiceTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<SlideshowDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        _provider = services.BuildServiceProvider();

        using (var scope = _provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<SlideshowDbContext>().Database.Migrate();
        }

        _playback = new ChannelPlaybackService(_slides, _provider.GetRequiredService<IServiceScopeFactory>(), _hub);

        _slides.SlidesByAlbum[1] = new[] { new SlideRef(10), new SlideRef(20), new SlideRef(30) };

        using var setupScope = _provider.CreateScope();
        var db = setupScope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = new Channel { Name = "kitchen", NormalizedName = "KITCHEN", CurrentAlbumId = 1, IsPaused = false };
        db.Channels.Add(channel);
        db.SaveChanges();
        _channelId = channel.Id;
    }

    [Fact]
    public async Task AdvanceMovesToNextSlideAndWrapsAround()
    {
        var first = await _playback.AdvanceAsync(_channelId, 1);
        Assert.Equal(10, first.CurrentSlideId);

        var second = await _playback.AdvanceAsync(_channelId, 1);
        Assert.Equal(20, second.CurrentSlideId);

        await _playback.AdvanceAsync(_channelId, 1); // -> 30
        var wrapped = await _playback.AdvanceAsync(_channelId, 1);
        Assert.Equal(10, wrapped.CurrentSlideId);
    }

    [Fact]
    public async Task JumpToSlideSetsExactSlide()
    {
        var state = await _playback.JumpAsync(_channelId, 30);
        Assert.Equal(30, state.CurrentSlideId);
    }

    [Fact]
    public async Task TogglePausePersistsAcrossReload()
    {
        var toggled = await _playback.TogglePauseAsync(_channelId);
        Assert.True(toggled.IsPaused);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var reloaded = await db.Channels.FindAsync(_channelId);
        Assert.True(reloaded!.IsPaused);
    }

    [Fact]
    public async Task SwitchingToAnAlbumWithNoSlidesResolvesToNoCurrentSlide()
    {
        var state = await _playback.SetAlbumAsync(_channelId, albumId: 999);
        Assert.Null(state.CurrentSlideId);
    }

    [Fact]
    public async Task EveryMutationBroadcastsToTheChannelGroup()
    {
        await _playback.AdvanceAsync(_channelId, 1);

        Assert.Contains(_hub.Sent, s => s.Group == SlideshowHub.GroupName(_channelId) && s.Method == "ChannelStateChanged");
    }

    [Fact]
    public async Task TickDoesNothingWhilePaused()
    {
        await _playback.TogglePauseAsync(_channelId); // now paused
        _hub.Sent.Clear();

        await _playback.TickAsync(_channelId);

        Assert.Empty(_hub.Sent);
    }

    [Fact]
    public async Task TickAdvancesOneSlideWhilePlaying()
    {
        await _playback.TickAsync(_channelId);
        var state = await _playback.GetStateAsync(_channelId);

        Assert.Equal(10, state.CurrentSlideId);
    }

    [Fact]
    public async Task StateSurvivesAFreshServiceInstanceOverTheSameDatabase()
    {
        await _playback.TogglePauseAsync(_channelId); // persist IsPaused = true

        var freshPlayback = new ChannelPlaybackService(_slides, _provider.GetRequiredService<IServiceScopeFactory>(), _hub);
        _hub.Sent.Clear();

        await freshPlayback.TickAsync(_channelId);

        Assert.Empty(_hub.Sent); // still paused, a fresh in-process runtime must not lose that
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SynologySlideshow.Api.Tests --filter ChannelPlaybackServiceTests`
Expected: build FAILS — none of `ISlideSource`, `SlideRef`, `ChannelPlaybackService`, `ChannelStateDto`, `SlideshowHub` exist yet.

- [ ] **Step 3: Implement the slide-source seam**

Create `SynologySlideshow.Api/Services/ISlideSource.cs`:

```csharp
namespace SynologySlideshow.Api.Services;

public record SlideRef(int Id);

public interface ISlideSource
{
    SlideRef[] GetSlides(int albumId);
}
```

Create `SynologySlideshow.Api/Services/SlideShowSlideSource.cs`:

```csharp
namespace SynologySlideshow.Api.Services;

public class SlideShowSlideSource : ISlideSource
{
    private readonly SlideShowService _slideShowService;

    public SlideShowSlideSource(SlideShowService slideShowService)
    {
        _slideShowService = slideShowService;
    }

    public SlideRef[] GetSlides(int albumId) =>
        _slideShowService.SlideShow.GetSlides(albumId).Select(s => new SlideRef(s.Id)).ToArray();
}
```

- [ ] **Step 4: Add a minimal `SlideshowHub` placeholder (filled in by Task 4)**

Create `SynologySlideshow.Api/Realtime/ChannelStateDto.cs`:

```csharp
namespace SynologySlideshow.Api.Realtime;

public class ChannelStateDto
{
    public int ChannelId { get; set; }
    public string Name { get; set; } = null!;
    public int? CurrentAlbumId { get; set; }
    public int? CurrentSlideId { get; set; }
    public bool IsPaused { get; set; }
}
```

Create `SynologySlideshow.Api/Realtime/SlideshowHub.cs`:

```csharp
using Microsoft.AspNetCore.SignalR;

namespace SynologySlideshow.Api.Realtime;

public class SlideshowHub : Hub
{
    public static string GroupName(int channelId) => $"channel:{channelId}";
}
```

- [ ] **Step 5: Implement `ChannelPlaybackService`**

Create `SynologySlideshow.Api/Services/ChannelPlaybackService.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Realtime;

namespace SynologySlideshow.Api.Services;

public class ChannelNotFoundException : Exception
{
    public ChannelNotFoundException(int channelId) : base($"Channel {channelId} was not found.")
    {
    }
}

internal class ChannelRuntimeState
{
    public int? CurrentSlideIndex;
}

public class ChannelPlaybackService
{
    private static readonly TimeSpan AdvanceInterval = TimeSpan.FromSeconds(30);

    private readonly ISlideSource _slideSource;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<SlideshowHub> _hubContext;
    private readonly ConcurrentDictionary<int, ChannelRuntimeState> _runtime = new();
    private readonly ConcurrentDictionary<int, Timer> _timers = new();

    public ChannelPlaybackService(ISlideSource slideSource, IServiceScopeFactory scopeFactory, IHubContext<SlideshowHub> hubContext)
    {
        _slideSource = slideSource;
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
    }

    public async Task<ChannelStateDto> GetStateAsync(int channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        return ToDto(channel, GetOrCreateRuntime(channelId));
    }

    public async Task<ChannelStateDto> AdvanceAsync(int channelId, int offset)
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

        return await BuildAndBroadcastAsync(channel, runtime);
    }

    public async Task<ChannelStateDto> JumpAsync(int channelId, int slideId)
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

        return await BuildAndBroadcastAsync(channel, runtime);
    }

    public async Task<ChannelStateDto> TogglePauseAsync(int channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        channel.IsPaused = !channel.IsPaused;
        await db.SaveChangesAsync();

        return await BuildAndBroadcastAsync(channel, GetOrCreateRuntime(channelId));
    }

    public async Task<ChannelStateDto> SetAlbumAsync(int channelId, int albumId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        channel.CurrentAlbumId = albumId;
        await db.SaveChangesAsync();

        var runtime = GetOrCreateRuntime(channelId);
        runtime.CurrentSlideIndex = null;

        return await BuildAndBroadcastAsync(channel, runtime);
    }

    public async Task TickAsync(int channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId);
        if (channel == null)
        {
            StopTimer(channelId);
            return;
        }
        if (channel.IsPaused) return;

        var runtime = GetOrCreateRuntime(channelId);
        var slides = GetSlides(channel.CurrentAlbumId);
        if (slides.Length == 0) return;

        var current = runtime.CurrentSlideIndex ?? -1;
        runtime.CurrentSlideIndex = (current + 1 + slides.Length) % slides.Length;

        await BuildAndBroadcastAsync(channel, runtime);
    }

    public void StartTimer(int channelId)
    {
        _timers.GetOrAdd(channelId, id => new Timer(OnTick, id, AdvanceInterval, AdvanceInterval));
    }

    public void StopTimer(int channelId)
    {
        if (_timers.TryRemove(channelId, out var timer))
        {
            timer.Dispose();
        }
        _runtime.TryRemove(channelId, out _);
    }

    private void OnTick(object? state) => _ = TickAsync((int)state!);

    private ChannelRuntimeState GetOrCreateRuntime(int channelId) =>
        _runtime.GetOrAdd(channelId, _ => new ChannelRuntimeState());

    private SlideRef[] GetSlides(int? albumId) =>
        albumId is int id ? _slideSource.GetSlides(id) : Array.Empty<SlideRef>();

    private async Task<ChannelStateDto> BuildAndBroadcastAsync(Channel channel, ChannelRuntimeState runtime)
    {
        var dto = ToDto(channel, runtime);
        await _hubContext.Clients.Group(SlideshowHub.GroupName(channel.Id)).SendAsync("ChannelStateChanged", dto);
        return dto;
    }

    private ChannelStateDto ToDto(Channel channel, ChannelRuntimeState runtime)
    {
        var slides = GetSlides(channel.CurrentAlbumId);
        int? currentSlideId = runtime.CurrentSlideIndex is int index && index >= 0 && index < slides.Length
            ? slides[index].Id
            : null;

        return new ChannelStateDto
        {
            ChannelId = channel.Id,
            Name = channel.Name,
            CurrentAlbumId = channel.CurrentAlbumId,
            CurrentSlideId = currentSlideId,
            IsPaused = channel.IsPaused
        };
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test SynologySlideshow.Api.Tests --filter ChannelPlaybackServiceTests`
Expected: PASS (8 tests).

- [ ] **Step 7: Wire timers into the controller and app startup**

Modify `SynologySlideshow.Api/Controllers/ChannelsController.cs`: add a `ChannelPlaybackService` dependency, start its timer on create, stop it on delete:

```csharp
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
```

Create `SynologySlideshow.Api/Services/ChannelTimerStartup.cs` (resumes every persisted channel's timer when the process starts, e.g. after a restart):

```csharp
using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;

namespace SynologySlideshow.Api.Services;

public class ChannelTimerStartup : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ChannelPlaybackService _playback;

    public ChannelTimerStartup(IServiceScopeFactory scopeFactory, ChannelPlaybackService playback)
    {
        _scopeFactory = scopeFactory;
        _playback = playback;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var ids = await db.Channels.Select(c => c.Id).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            _playback.StartTimer(id);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

In `SynologySlideshow.Api/Program.cs`, register the new services. Add these lines right after the `AddDbContext<SlideshowDbContext>` call added in Task 1:

```csharp
builder.Services.AddSignalR();
builder.Services.AddSingleton<ISlideSource, SlideShowSlideSource>();
builder.Services.AddSingleton<ChannelPlaybackService>();
builder.Services.AddHostedService<ChannelTimerStartup>();
```

(`AddSignalR()` is needed here even though the hub itself isn't mapped until Task 4, because `ChannelPlaybackService` already depends on `IHubContext<SlideshowHub>`.)

- [ ] **Step 8: Run the full test suite to verify nothing regressed**

Run: `dotnet test SynologySlideshow.Api.Tests`
Expected: PASS (all tests from Tasks 1–3).

- [ ] **Step 9: Commit**

```bash
git add SynologySlideshow.Api/Services SynologySlideshow.Api/Realtime SynologySlideshow.Api/Controllers/ChannelsController.cs SynologySlideshow.Api/Program.cs SynologySlideshow.Api.Tests
git commit -m "feat: add server-owned channel playback state and advance timer"
```

---

## Task 4: SignalR hub — join, control, and broadcast

**Files:**
- Modify: `SynologySlideshow.Api/Realtime/SlideshowHub.cs`
- Modify: `SynologySlideshow.Api/Program.cs`
- Create: `SynologySlideshow.Api.Tests/Realtime/SlideshowHubTests.cs`

**Interfaces:**
- Consumes: `ChannelPlaybackService` (Task 3), `SlideshowDbContext` (Task 1), `ChannelsController`'s REST endpoints (Task 2, used by tests to create channels).
- Produces: hub endpoint `/hub/slideshow` with client-invokable methods `JoinChannel(string channelName): Task<ChannelStateDto?>` (null if the name doesn't exist), `LeaveChannel(int channelId): Task`, `RequestNextSlide(int channelId): Task<ChannelStateDto>`, `RequestPreviousSlide(int channelId): Task<ChannelStateDto>`, `RequestJumpToSlide(int channelId, int slideId): Task<ChannelStateDto>`, `RequestTogglePause(int channelId): Task<ChannelStateDto>`, `RequestSwitchAlbum(int channelId, int albumId): Task<ChannelStateDto>`; and the server-pushed event `"ChannelStateChanged"` carrying a `ChannelStateDto`. This is the exact contract Plan 2 (client viewing) and Plan 3 (admin) call against.

- [ ] **Step 1: Write the failing tests**

Create `SynologySlideshow.Api.Tests/Realtime/SlideshowHubTests.cs`:

```csharp
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SynologySlideshow.Api.Controllers;
using SynologySlideshow.Api.Realtime;
using Xunit;

namespace SynologySlideshow.Api.Tests.Realtime;

public class SlideshowHubTests : IClassFixture<SlideshowApiFactory>, IAsyncLifetime
{
    private readonly SlideshowApiFactory _factory;
    private HubConnection _connectionA = null!;
    private HubConnection _connectionB = null!;

    public SlideshowHubTests(SlideshowApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _connectionA = BuildConnection();
        _connectionB = BuildConnection();
        await _connectionA.StartAsync();
        await _connectionB.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _connectionA.DisposeAsync();
        await _connectionB.DisposeAsync();
    }

    private HubConnection BuildConnection() =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hub/slideshow"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

    private async Task<ChannelSummary> CreateChannelAsync(string name)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/channels", new CreateChannelRequest { Name = name });
        return (await response.Content.ReadFromJsonAsync<ChannelSummary>())!;
    }

    [Fact]
    public async Task JoiningAnExistingChannelReturnsItsCurrentState()
    {
        var channel = await CreateChannelAsync("hub-join-test");

        var state = await _connectionA.InvokeAsync<ChannelStateDto?>("JoinChannel", "hub-join-test");

        Assert.NotNull(state);
        Assert.Equal(channel.Id, state!.ChannelId);
    }

    [Fact]
    public async Task JoiningAnUnknownChannelReturnsNull()
    {
        var state = await _connectionA.InvokeAsync<ChannelStateDto?>("JoinChannel", "does-not-exist");

        Assert.Null(state);
    }

    [Fact]
    public async Task ControllingOneConnectionBroadcastsToEveryConnectionInTheChannel()
    {
        var channel = await CreateChannelAsync("hub-broadcast-test");

        var receivedByB = new List<ChannelStateDto>();
        _connectionB.On<ChannelStateDto>("ChannelStateChanged", dto => receivedByB.Add(dto));

        await _connectionA.InvokeAsync("JoinChannel", "hub-broadcast-test");
        await _connectionB.InvokeAsync("JoinChannel", "hub-broadcast-test");

        await _connectionA.InvokeAsync("RequestTogglePause", channel.Id);

        await WaitUntilAsync(() => receivedByB.Any(s => s.IsPaused), TimeSpan.FromSeconds(5));
        Assert.Contains(receivedByB, s => s.IsPaused);
    }

    [Fact]
    public async Task RequestingAControlOnAnUnknownChannelFaultsOnlyThatCallNotTheConnection()
    {
        await Assert.ThrowsAsync<Exception>(() => _connectionA.InvokeAsync<ChannelStateDto>("RequestNextSlide", 999999));

        // the connection must still be usable afterwards
        var channel = await CreateChannelAsync("hub-recovers-test");
        var state = await _connectionA.InvokeAsync<ChannelStateDto?>("JoinChannel", "hub-recovers-test");
        Assert.NotNull(state);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SynologySlideshow.Api.Tests --filter SlideshowHubTests`
Expected: build/run FAILS — `/hub/slideshow` isn't mapped and the hub has no methods yet.

- [ ] **Step 3: Implement the hub**

Replace `SynologySlideshow.Api/Realtime/SlideshowHub.cs`:

```csharp
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Services;

namespace SynologySlideshow.Api.Realtime;

public class SlideshowHub : Hub
{
    private readonly SlideshowDbContext _db;
    private readonly ChannelPlaybackService _playback;

    public SlideshowHub(SlideshowDbContext db, ChannelPlaybackService playback)
    {
        _db = db;
        _playback = playback;
    }

    public static string GroupName(int channelId) => $"channel:{channelId}";

    public async Task<ChannelStateDto?> JoinChannel(string channelName)
    {
        var channel = await _db.Channels.SingleOrDefaultAsync(c => c.Name == channelName);
        if (channel == null) return null;

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(channel.Id));
        return await _playback.GetStateAsync(channel.Id);
    }

    public Task LeaveChannel(int channelId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(channelId));

    public Task<ChannelStateDto> RequestNextSlide(int channelId) => _playback.AdvanceAsync(channelId, 1);

    public Task<ChannelStateDto> RequestPreviousSlide(int channelId) => _playback.AdvanceAsync(channelId, -1);

    public Task<ChannelStateDto> RequestJumpToSlide(int channelId, int slideId) => _playback.JumpAsync(channelId, slideId);

    public Task<ChannelStateDto> RequestTogglePause(int channelId) => _playback.TogglePauseAsync(channelId);

    public Task<ChannelStateDto> RequestSwitchAlbum(int channelId, int albumId) => _playback.SetAlbumAsync(channelId, albumId);
}
```

Map the hub in `SynologySlideshow.Api/Program.cs` — add this line right after `app.MapControllers();`:

```csharp
app.MapHub<SlideshowHub>("/hub/slideshow");
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SynologySlideshow.Api.Tests --filter SlideshowHubTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Run the full backend test suite**

Run: `dotnet test SynologySlideshow.Api.Tests`
Expected: PASS (all tests across Tasks 1–4).

- [ ] **Step 6: Commit**

```bash
git add SynologySlideshow.Api/Realtime/SlideshowHub.cs SynologySlideshow.Api/Program.cs SynologySlideshow.Api.Tests/Realtime
git commit -m "feat: add SlideshowHub for channel join/control/broadcast"
```
