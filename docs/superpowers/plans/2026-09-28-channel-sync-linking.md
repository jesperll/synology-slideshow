# Channel Sync-Linking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the admin link two or more Channels into a group so that controlling any one of them (pause, album switch, next/previous, jump-to-slide — from admin or from any of their own local viewers) controls the whole group identically, until explicitly unlinked.

**Architecture:** A new in-memory `SyncGroupService` tracks group membership (never persisted — groups are explicitly temporary, per the spec). `ChannelPlaybackService`'s existing single-channel mutation methods (from the core-infrastructure plan) become private `...OneAsync` implementations; their public names become thin fan-out wrappers that resolve the calling channel's group members and apply the same operation to every one of them. Linking harmonizes every other member onto the seed channel's current album/slide/pause state at link time. The advance timer gets a "lowest channel id in the group drives the tick" rule so a linked group's independently-created per-channel timers don't double-advance or drift apart.

**Tech Stack:** No new packages — this plan is entirely additive C#/TypeScript on top of the previous two plans.

**Spec:** `docs/superpowers/plans/2026-09-28-channel-feature-spec.md`

## Global Constraints

- Sync groups are never persisted — an admin unlink, or an incidental server restart (which drops all in-memory state anyway), has the same effect: every member simply continues independently from wherever it landed.
- A channel can belong to at most one active group at a time.
- There is no "leader" for control purposes once linked — every member is symmetric; the channel a group was formed *from* has no special standing afterward.

## Review Focus

- Toggling pause from one member must set the *entire group* to one shared new value (all-paused or all-playing) — never let each member flip its own current bit independently, which could produce a mixed group even though a single "toggle" was requested.
- Each channel in a group still has its own independently-created advance timer; once linked, only one of them may actually be allowed to drive the shared advance on each tick, or the group will double-advance and drift apart within the first 30-second interval.
- Linking a channel that's already in another active group must fail with a clear error and must not touch the existing group at all.
- Linking with fewer than two channels, or with a channel id that doesn't exist, must fail cleanly with no partial effect.
- Unlinking any *one* member must dissolve the whole group — every member (not just the one requested) must return to independent operation.

---

## Task 1: `SyncGroupService` — in-memory group membership

**Files:**
- Create: `SynologySlideshow.Api/Services/SyncGroupService.cs`
- Create: `SynologySlideshow.Api.Tests/Services/SyncGroupServiceTests.cs`

**Interfaces:**
- Produces: `SynologySlideshow.Api.Services.SyncGroupService` with `(bool Success, string? Error) Link(IReadOnlyCollection<int> channelIds)`, `void Unlink(int channelId)`, `IReadOnlySet<int> GetGroupMembers(int channelId)` (returns a set containing just `channelId` itself when it isn't in any group). Task 2 builds `ChannelPlaybackService`'s fan-out entirely on top of this.

- [ ] **Step 1: Write the failing tests**

Create `SynologySlideshow.Api.Tests/Services/SyncGroupServiceTests.cs`:

```csharp
using SynologySlideshow.Api.Services;
using Xunit;

namespace SynologySlideshow.Api.Tests.Services;

public class SyncGroupServiceTests
{
    [Fact]
    public void LinkingTwoChannelsGroupsThemTogether()
    {
        var service = new SyncGroupService();

        var (success, error) = service.Link(new[] { 1, 2 });

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(new HashSet<int> { 1, 2 }, service.GetGroupMembers(1));
        Assert.Equal(new HashSet<int> { 1, 2 }, service.GetGroupMembers(2));
    }

    [Fact]
    public void LinkingFewerThanTwoChannelsFails()
    {
        var service = new SyncGroupService();

        var (success, error) = service.Link(new[] { 1 });

        Assert.False(success);
        Assert.NotNull(error);
    }

    [Fact]
    public void LinkingAChannelAlreadyInAGroupFailsAndLeavesTheOriginalGroupIntact()
    {
        var service = new SyncGroupService();
        service.Link(new[] { 1, 2 });

        var (success, error) = service.Link(new[] { 2, 3 });

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Equal(new HashSet<int> { 1, 2 }, service.GetGroupMembers(1));
    }

    [Fact]
    public void AChannelNotInAnyGroupReturnsItselfAsItsOnlyMember()
    {
        var service = new SyncGroupService();

        Assert.Equal(new HashSet<int> { 42 }, service.GetGroupMembers(42));
    }

    [Fact]
    public void UnlinkingAnyMemberDissolvesTheWholeGroup()
    {
        var service = new SyncGroupService();
        service.Link(new[] { 1, 2, 3 });

        service.Unlink(2);

        Assert.Equal(new HashSet<int> { 1 }, service.GetGroupMembers(1));
        Assert.Equal(new HashSet<int> { 2 }, service.GetGroupMembers(2));
        Assert.Equal(new HashSet<int> { 3 }, service.GetGroupMembers(3));
    }

    [Fact]
    public void GroupsOfMoreThanTwoAreSupported()
    {
        var service = new SyncGroupService();

        var (success, _) = service.Link(new[] { 1, 2, 3 });

        Assert.True(success);
        Assert.Equal(new HashSet<int> { 1, 2, 3 }, service.GetGroupMembers(3));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SynologySlideshow.Api.Tests --filter SyncGroupServiceTests`
Expected: build FAILS — `SyncGroupService` doesn't exist yet.

- [ ] **Step 3: Implement it**

Create `SynologySlideshow.Api/Services/SyncGroupService.cs`:

```csharp
using System.Collections.Concurrent;

namespace SynologySlideshow.Api.Services;

public class SyncGroupService
{
    private readonly ConcurrentDictionary<int, HashSet<int>> _groupsByChannel = new();

    public (bool Success, string? Error) Link(IReadOnlyCollection<int> channelIds)
    {
        var distinct = channelIds.Distinct().ToList();
        if (distinct.Count < 2)
            return (false, "A sync group needs at least two channels.");

        foreach (var id in distinct)
        {
            if (_groupsByChannel.ContainsKey(id))
                return (false, $"Channel {id} is already in a sync group.");
        }

        var members = new HashSet<int>(distinct);
        foreach (var id in distinct)
        {
            _groupsByChannel[id] = members;
        }

        return (true, null);
    }

    public void Unlink(int channelId)
    {
        if (_groupsByChannel.TryRemove(channelId, out var members))
        {
            foreach (var member in members)
            {
                if (member != channelId)
                {
                    _groupsByChannel.TryRemove(member, out _);
                }
            }
        }
    }

    public IReadOnlySet<int> GetGroupMembers(int channelId) =>
        _groupsByChannel.TryGetValue(channelId, out var members) ? members : new HashSet<int> { channelId };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SynologySlideshow.Api.Tests --filter SyncGroupServiceTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add SynologySlideshow.Api/Services/SyncGroupService.cs SynologySlideshow.Api.Tests/Services/SyncGroupServiceTests.cs
git commit -m "feat: add in-memory sync group membership tracking"
```

---

## Task 2: Fan-out playback control, link/unlink, and the timer desync fix

**Files:**
- Modify: `SynologySlideshow.Api/Services/ChannelPlaybackService.cs`
- Create: `SynologySlideshow.Api/Realtime/LinkResult.cs`
- Modify: `SynologySlideshow.Api/Realtime/SlideshowHub.cs`
- Modify: `SynologySlideshow.Api/Realtime/AdminSnapshot.cs`
- Modify: `SynologySlideshow.Api/Services/AdminSnapshotService.cs`
- Modify: `SynologySlideshow.Api/Program.cs`
- Create: `SynologySlideshow.Api.Tests/Services/ChannelPlaybackServiceLinkingTests.cs`
- Create: `SynologySlideshow.Api.Tests/Realtime/SlideshowHubLinkingTests.cs`

**Interfaces:**
- Consumes: `SyncGroupService` (Task 1); `ChannelStateDto`, `ISlideSource`, `SlideRef` (core-infrastructure plan); `PresenceTracker`, `AdminSnapshotService` (admin-interface plan).
- Produces: `ChannelPlaybackService.LinkAsync(IReadOnlyCollection<int> channelIds): Task<LinkResult>`, `UnlinkAsync(int channelId): Task<ChannelStateDto>`, `GetGroupMembers(int channelId): IReadOnlySet<int>`; `SynologySlideshow.Api.Realtime.LinkResult(bool Success, string? Error)`; hub methods `RequestLinkChannels(int[] channelIds): Task<LinkResult>`, `RequestUnlinkChannel(int channelId): Task<ChannelStateDto>`; `AdminChannelEntry` gains `int[] LinkedChannelIds` — the exact contract Task 3 (frontend) consumes.

- [ ] **Step 1: Write the failing service-level tests**

Create `SynologySlideshow.Api.Tests/Services/ChannelPlaybackServiceLinkingTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Services;
using SynologySlideshow.Api.Tests.TestDoubles;
using Xunit;

namespace SynologySlideshow.Api.Tests.Services;

public class ChannelPlaybackServiceLinkingTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"linking-test-{Guid.NewGuid()}.db");
    private readonly ServiceProvider _provider;
    private readonly FakeHubContext _hub = new();
    private readonly FakeSlideSource _slides = new();
    private readonly SyncGroupService _syncGroups = new();
    private readonly ChannelPlaybackService _playback;
    private readonly int _channelA;
    private readonly int _channelB;

    public ChannelPlaybackServiceLinkingTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<SlideshowDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        _provider = services.BuildServiceProvider();

        using (var scope = _provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<SlideshowDbContext>().Database.Migrate();
        }

        _playback = new ChannelPlaybackService(_slides, _provider.GetRequiredService<IServiceScopeFactory>(), _hub, _syncGroups);

        _slides.SlidesByAlbum[1] = new[] { new SlideRef(10), new SlideRef(20), new SlideRef(30) };
        _slides.SlidesByAlbum[2] = new[] { new SlideRef(100), new SlideRef(200) };

        using var setupScope = _provider.CreateScope();
        var db = setupScope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var a = new Channel { Name = "kitchen", NormalizedName = "KITCHEN", CurrentAlbumId = 1, IsPaused = false };
        var b = new Channel { Name = "living-room", NormalizedName = "LIVING-ROOM", CurrentAlbumId = 2, IsPaused = true };
        db.Channels.AddRange(a, b);
        db.SaveChanges();
        _channelA = a.Id;
        _channelB = b.Id;
    }

    [Fact]
    public async Task LinkingHarmonizesTheOtherMembersOntoTheSeedsAlbumSlideAndPauseState()
    {
        await _playback.AdvanceAsync(_channelA, 1); // seed now at slide 10

        var result = await _playback.LinkAsync(new[] { _channelA, _channelB });

        Assert.True(result.Success);
        var stateB = await _playback.GetStateAsync(_channelB);
        Assert.Equal(1, stateB.CurrentAlbumId);
        Assert.Equal(10, stateB.CurrentSlideId);
        Assert.False(stateB.IsPaused);
    }

    [Fact]
    public async Task TogglingPauseOnOneMemberSetsTheWholeGroupToTheSameNewValue()
    {
        await _playback.LinkAsync(new[] { _channelA, _channelB });

        await _playback.TogglePauseAsync(_channelA);

        var stateA = await _playback.GetStateAsync(_channelA);
        var stateB = await _playback.GetStateAsync(_channelB);
        Assert.True(stateA.IsPaused);
        Assert.True(stateB.IsPaused);
    }

    [Fact]
    public async Task AdvancingOneMemberAdvancesEveryMember()
    {
        await _playback.LinkAsync(new[] { _channelA, _channelB });

        var resultA = await _playback.AdvanceAsync(_channelA, 1);
        var stateB = await _playback.GetStateAsync(_channelB);

        Assert.Equal(10, resultA.CurrentSlideId);
        Assert.Equal(10, stateB.CurrentSlideId);
    }

    [Fact]
    public async Task LinkingFailsWhenAChannelDoesNotExist()
    {
        var result = await _playback.LinkAsync(new[] { _channelA, 999999 });

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task UnlinkingDissolvesTheWholeGroupAndEachContinuesIndependently()
    {
        await _playback.LinkAsync(new[] { _channelA, _channelB });

        await _playback.UnlinkAsync(_channelA);
        await _playback.AdvanceAsync(_channelA, 1);

        var stateB = await _playback.GetStateAsync(_channelB);
        Assert.Null(stateB.CurrentSlideId);
    }

    [Fact]
    public async Task OnlyTheLowestIdMembersTickActuallyAdvancesTheLinkedGroup()
    {
        await _playback.LinkAsync(new[] { _channelA, _channelB });

        _hub.Sent.Clear();
        await _playback.TickAsync(Math.Max(_channelA, _channelB));

        Assert.Empty(_hub.Sent);

        await _playback.TickAsync(Math.Min(_channelA, _channelB));

        var stateA = await _playback.GetStateAsync(_channelA);
        var stateB = await _playback.GetStateAsync(_channelB);
        Assert.Equal(10, stateA.CurrentSlideId);
        Assert.Equal(10, stateB.CurrentSlideId);
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SynologySlideshow.Api.Tests --filter ChannelPlaybackServiceLinkingTests`
Expected: build FAILS — `ChannelPlaybackService`'s constructor doesn't take a `SyncGroupService` yet, and `LinkAsync`/`UnlinkAsync` don't exist.

- [ ] **Step 3: Add `LinkResult` and rewrite `ChannelPlaybackService`**

Create `SynologySlideshow.Api/Realtime/LinkResult.cs`:

```csharp
namespace SynologySlideshow.Api.Realtime;

public record LinkResult(bool Success, string? Error);
```

Replace `SynologySlideshow.Api/Services/ChannelPlaybackService.cs` in full:

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
    private readonly SyncGroupService _syncGroups;
    private readonly ConcurrentDictionary<int, ChannelRuntimeState> _runtime = new();
    private readonly ConcurrentDictionary<int, Timer> _timers = new();

    public ChannelPlaybackService(
        ISlideSource slideSource,
        IServiceScopeFactory scopeFactory,
        IHubContext<SlideshowHub> hubContext,
        SyncGroupService syncGroups)
    {
        _slideSource = slideSource;
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _syncGroups = syncGroups;
    }

    public async Task<ChannelStateDto> GetStateAsync(int channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        return ToDto(channel, GetOrCreateRuntime(channelId));
    }

    public Task<ChannelStateDto> AdvanceAsync(int channelId, int offset) =>
        ApplyToGroupAsync(channelId, id => AdvanceOneAsync(id, offset));

    public Task<ChannelStateDto> JumpAsync(int channelId, int slideId) =>
        ApplyToGroupAsync(channelId, id => JumpOneAsync(id, slideId));

    public async Task<ChannelStateDto> TogglePauseAsync(int channelId)
    {
        var current = await GetStateAsync(channelId);
        var target = !current.IsPaused;
        return await ApplyToGroupAsync(channelId, id => SetPausedOneAsync(id, target));
    }

    public Task<ChannelStateDto> SetAlbumAsync(int channelId, int albumId) =>
        ApplyToGroupAsync(channelId, id => SetAlbumOneAsync(id, albumId));

    public async Task<LinkResult> LinkAsync(IReadOnlyCollection<int> channelIds)
    {
        var distinctIds = channelIds.Distinct().ToList();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var existingCount = await db.Channels.CountAsync(c => distinctIds.Contains(c.Id));
        if (existingCount != distinctIds.Count)
            return new LinkResult(false, "One or more channels don't exist.");

        var (success, error) = _syncGroups.Link(distinctIds);
        if (!success) return new LinkResult(false, error);

        var seedState = await GetStateAsync(distinctIds[0]);
        foreach (var memberId in distinctIds.Skip(1))
        {
            if (seedState.CurrentAlbumId is int albumId)
            {
                await SetAlbumOneAsync(memberId, albumId);
            }
            if (seedState.CurrentSlideId is int slideId)
            {
                await JumpOneAsync(memberId, slideId);
            }
            var memberState = await GetStateAsync(memberId);
            if (memberState.IsPaused != seedState.IsPaused)
            {
                await SetPausedOneAsync(memberId, seedState.IsPaused);
            }
        }

        return new LinkResult(true, null);
    }

    public Task<ChannelStateDto> UnlinkAsync(int channelId)
    {
        _syncGroups.Unlink(channelId);
        return GetStateAsync(channelId);
    }

    public IReadOnlySet<int> GetGroupMembers(int channelId) => _syncGroups.GetGroupMembers(channelId);

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

        var members = _syncGroups.GetGroupMembers(channelId);
        if (members.Count > 1 && members.Min() != channelId)
        {
            return; // a lower-id member's timer already drives this group's advance
        }

        var slides = GetSlides(channel.CurrentAlbumId);
        if (slides.Length == 0) return;

        foreach (var memberId in members)
        {
            await AdvanceOneAsync(memberId, 1);
        }
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

    private async Task<ChannelStateDto> ApplyToGroupAsync(int originId, Func<int, Task<ChannelStateDto>> applyOne)
    {
        var members = _syncGroups.GetGroupMembers(originId);
        ChannelStateDto? originResult = null;
        foreach (var memberId in members)
        {
            var dto = await applyOne(memberId);
            if (memberId == originId) originResult = dto;
        }
        return originResult!;
    }

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

        return await BuildAndBroadcastAsync(channel, runtime);
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

        return await BuildAndBroadcastAsync(channel, runtime);
    }

    private async Task<ChannelStateDto> SetPausedOneAsync(int channelId, bool isPaused)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlideshowDbContext>();
        var channel = await db.Channels.FindAsync(channelId) ?? throw new ChannelNotFoundException(channelId);

        channel.IsPaused = isPaused;
        await db.SaveChangesAsync();

        return await BuildAndBroadcastAsync(channel, GetOrCreateRuntime(channelId));
    }

    private async Task<ChannelStateDto> SetAlbumOneAsync(int channelId, int albumId)
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

    private ChannelRuntimeState GetOrCreateRuntime(int channelId) =>
        _runtime.GetOrAdd(channelId, _ => new ChannelRuntimeState());

    private SlideRef[] GetSlides(int? albumId) =>
        albumId is int id ? _slideSource.GetSlides(id) : Array.Empty<SlideRef>();

    private async Task<ChannelStateDto> BuildAndBroadcastAsync(Channel channel, ChannelRuntimeState runtime)
    {
        var dto = ToDto(channel, runtime);
        await _hubContext.Clients.Group(SlideshowHub.GroupName(channel.Id)).SendAsync("ChannelStateChanged", dto);
        await _hubContext.Clients.Group(SlideshowHub.AdminGroupName).SendAsync("ChannelStateChanged", dto);
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

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SynologySlideshow.Api.Tests --filter ChannelPlaybackServiceLinkingTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Write the failing hub-level integration tests**

Create `SynologySlideshow.Api.Tests/Realtime/SlideshowHubLinkingTests.cs`:

```csharp
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SynologySlideshow.Api.Controllers;
using SynologySlideshow.Api.Realtime;
using Xunit;

namespace SynologySlideshow.Api.Tests.Realtime;

public class SlideshowHubLinkingTests : IClassFixture<SlideshowApiFactory>, IAsyncLifetime
{
    private readonly SlideshowApiFactory _factory;
    private HubConnection _connectionA = null!;
    private HubConnection _connectionB = null!;

    public SlideshowHubLinkingTests(SlideshowApiFactory factory)
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
    public async Task LinkingTwoChannelsThenPausingOnePausesBoth()
    {
        var channelA = await CreateChannelAsync("link-hub-a");
        var channelB = await CreateChannelAsync("link-hub-b");

        var linkResult = await _connectionA.InvokeAsync<LinkResult>("RequestLinkChannels", new[] { channelA.Id, channelB.Id });
        Assert.True(linkResult.Success);

        await _connectionA.InvokeAsync("JoinChannel", "link-hub-a");
        await _connectionB.InvokeAsync("JoinChannel", "link-hub-b");

        var receivedByB = new List<ChannelStateDto>();
        _connectionB.On<ChannelStateDto>("ChannelStateChanged", receivedByB.Add);

        await _connectionA.InvokeAsync("RequestTogglePause", channelA.Id);

        await WaitUntilAsync(() => receivedByB.Any(s => s.ChannelId == channelB.Id && s.IsPaused), TimeSpan.FromSeconds(5));
        Assert.Contains(receivedByB, s => s.ChannelId == channelB.Id && s.IsPaused);
    }

    [Fact]
    public async Task UnlinkingReturnsAChannelToIndependentControl()
    {
        var channelA = await CreateChannelAsync("unlink-hub-a");
        var channelB = await CreateChannelAsync("unlink-hub-b");
        await _connectionA.InvokeAsync<LinkResult>("RequestLinkChannels", new[] { channelA.Id, channelB.Id });

        await _connectionA.InvokeAsync("RequestUnlinkChannel", channelA.Id);

        var receivedByB = new List<ChannelStateDto>();
        _connectionB.On<ChannelStateDto>("ChannelStateChanged", receivedByB.Add);

        await _connectionA.InvokeAsync("RequestTogglePause", channelA.Id);
        await Task.Delay(200);

        Assert.DoesNotContain(receivedByB, s => s.ChannelId == channelB.Id);
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

- [ ] **Step 6: Run the tests to verify they fail**

Run: `dotnet test SynologySlideshow.Api.Tests --filter SlideshowHubLinkingTests`
Expected: build FAILS — the hub has no `RequestLinkChannels`/`RequestUnlinkChannel` methods yet.

- [ ] **Step 7: Wire the hub methods and extend the admin snapshot**

In `SynologySlideshow.Api/Realtime/SlideshowHub.cs`, add these two methods (anywhere alongside the other `Request*` methods):

```csharp
    public Task<LinkResult> RequestLinkChannels(int[] channelIds) => _playback.LinkAsync(channelIds);

    public Task<ChannelStateDto> RequestUnlinkChannel(int channelId) => _playback.UnlinkAsync(channelId);
```

In `SynologySlideshow.Api/Realtime/AdminSnapshot.cs`, add the linked-ids field:

```csharp
namespace SynologySlideshow.Api.Realtime;

public record AdminChannelEntry(int ChannelId, string Name, int? CurrentAlbumId, int? CurrentSlideId, bool IsPaused, int ViewerCount, int[] LinkedChannelIds);

public record AdminSnapshot(int AnonymousCount, AdminChannelEntry[] Channels);
```

In `SynologySlideshow.Api/Services/AdminSnapshotService.cs`, populate it:

```csharp
    public async Task<AdminSnapshot> BuildAsync()
    {
        var channels = await _db.Channels.OrderBy(c => c.Name).ToListAsync();
        var entries = new List<AdminChannelEntry>();
        foreach (var channel in channels)
        {
            var state = await _playback.GetStateAsync(channel.Id);
            var linkedWith = _playback.GetGroupMembers(channel.Id).Where(id => id != channel.Id).ToArray();
            entries.Add(new AdminChannelEntry(
                channel.Id,
                channel.Name,
                state.CurrentAlbumId,
                state.CurrentSlideId,
                state.IsPaused,
                _presence.GetViewerCount(channel.Id),
                linkedWith));
        }
        return new AdminSnapshot(_presence.GetAnonymousCount(), entries.ToArray());
    }
```

In `SynologySlideshow.Api/Program.cs`, register the new singleton — add this line next to the other `AddSingleton` registrations from the earlier plans:

```csharp
builder.Services.AddSingleton<SyncGroupService>();
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test SynologySlideshow.Api.Tests --filter SlideshowHubLinkingTests`
Expected: PASS (2 tests).

- [ ] **Step 9: Run the full backend test suite**

Run: `dotnet test SynologySlideshow.Api.Tests`
Expected: PASS (every test across all plans so far).

- [ ] **Step 10: Commit**

```bash
git add SynologySlideshow.Api/Services/ChannelPlaybackService.cs SynologySlideshow.Api/Realtime/LinkResult.cs SynologySlideshow.Api/Realtime/SlideshowHub.cs SynologySlideshow.Api/Realtime/AdminSnapshot.cs SynologySlideshow.Api/Services/AdminSnapshotService.cs SynologySlideshow.Api/Program.cs SynologySlideshow.Api.Tests
git commit -m "feat: fan out channel control to sync-linked groups"
```

---

## Task 3: Admin UI — select, link, and unlink

**Files:**
- Modify: `SynologySlideshow.Web/src/types/index.ts`
- Modify: `SynologySlideshow.Web/src/hooks/useAdminConnection.ts`
- Modify: `SynologySlideshow.Web/src/hooks/useAdminConnection.test.ts`
- Modify: `SynologySlideshow.Web/src/components/AdminPage.tsx`
- Modify: `SynologySlideshow.Web/src/components/AdminPage.test.tsx`

**Interfaces:**
- Consumes: `RequestLinkChannels`/`RequestUnlinkChannel` hub methods and the extended `AdminChannelEntry` (Task 2).
- Produces: `useAdminConnection().requestLink(channelIds: number[]): Promise<LinkResult>` and `.requestUnlink(channelId: number): Promise<void>`; a "Link selected" control and per-row "Unlink" button in `AdminPage`.

- [ ] **Step 1: Write the failing hook tests**

Add these two tests to `SynologySlideshow.Web/src/hooks/useAdminConnection.test.ts` (append inside the existing `describe` block, after the last existing test) — and extend `FakeConnection` so `invoke('RequestLinkChannels', ...)` returns a configurable result:

```ts
class FakeConnection implements HubConnectionLike {
  handlers = new Map<string, (...args: any[]) => void>();
  invokeCalls: [string, any[]][] = [];
  snapshot: AdminSnapshot = { anonymousCount: 0, channels: [] };
  linkResult: { success: boolean; error: string | null } = { success: true, error: null };

  on(methodName: string, callback: (...args: any[]) => void): void {
    this.handlers.set(methodName, callback);
  }

  async invoke<T = void>(methodName: string, ...args: any[]): Promise<T> {
    this.invokeCalls.push([methodName, args]);
    if (methodName === 'JoinAdmin') {
      return this.snapshot as unknown as T;
    }
    if (methodName === 'RequestLinkChannels') {
      return this.linkResult as unknown as T;
    }
    return undefined as unknown as T;
  }

  async start(): Promise<void> {}
  async stop(): Promise<void> {}

  emit(methodName: string, payload: unknown) {
    this.handlers.get(methodName)?.(payload);
  }
}
```

(This replaces the earlier `FakeConnection` definition at the top of the file — same shape, with the two additions above.)

```ts
  it('returns the link result and forwards the channel ids', async () => {
    const fake = new FakeConnection();
    fake.snapshot = { anonymousCount: 0, channels: [] };
    fake.linkResult = { success: true, error: null };

    const { result } = renderHook(() => useAdminConnection(() => fake));
    await waitFor(() => expect(result.current.snapshot).not.toBeNull());

    const outcome = await result.current.requestLink([1, 2]);

    expect(outcome).toEqual({ success: true, error: null });
    expect(fake.invokeCalls).toContainEqual(['RequestLinkChannels', [[1, 2]]]);
  });

  it('sends the channel id when requesting an unlink', async () => {
    const fake = new FakeConnection();
    fake.snapshot = { anonymousCount: 0, channels: [] };

    const { result } = renderHook(() => useAdminConnection(() => fake));
    await waitFor(() => expect(result.current.snapshot).not.toBeNull());

    await result.current.requestUnlink(4);

    expect(fake.invokeCalls).toContainEqual(['RequestUnlinkChannel', [4]]);
  });
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd SynologySlideshow.Web && npm test -- useAdminConnection`
Expected: FAIL — `requestLink`/`requestUnlink` don't exist on the hook's return value yet.

- [ ] **Step 3: Extend the types and the hook**

Add to `SynologySlideshow.Web/src/types/index.ts`:

```ts
export interface LinkResult {
  success: boolean;
  error: string | null;
}
```

and change `AdminChannelEntry` to add the new field:

```ts
export interface AdminChannelEntry {
  channelId: number;
  name: string;
  currentAlbumId: number | null;
  currentSlideId: number | null;
  isPaused: boolean;
  viewerCount: number;
  linkedChannelIds: number[];
}
```

Replace `SynologySlideshow.Web/src/hooks/useAdminConnection.ts` in full:

```ts
import { useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { AdminSnapshot, ChannelState, LinkResult } from '../types';
import { ConnectionFactory, HubConnectionLike } from './useChannelConnection';

const defaultFactory: ConnectionFactory = () =>
  new signalR.HubConnectionBuilder()
    .withUrl('/hub/slideshow')
    .withAutomaticReconnect()
    .build();

export interface AdminConnectionResult {
  snapshot: AdminSnapshot | null;
  requestNext(channelId: number): Promise<void>;
  requestPrevious(channelId: number): Promise<void>;
  requestJump(channelId: number, slideId: number): Promise<void>;
  requestTogglePause(channelId: number): Promise<void>;
  requestSwitchAlbum(channelId: number, albumId: number): Promise<void>;
  requestLink(channelIds: number[]): Promise<LinkResult>;
  requestUnlink(channelId: number): Promise<void>;
}

export function useAdminConnection(factory: ConnectionFactory = defaultFactory): AdminConnectionResult {
  const [snapshot, setSnapshot] = useState<AdminSnapshot | null>(null);
  const connectionRef = useRef<HubConnectionLike | null>(null);

  useEffect(() => {
    let cancelled = false;
    const connection = factory();
    connectionRef.current = connection;

    connection.on('PresenceChanged', (payload: AdminSnapshot) => {
      if (!cancelled) setSnapshot(payload);
    });

    connection.on('ChannelStateChanged', (payload: ChannelState) => {
      if (cancelled) return;
      setSnapshot((current) => {
        if (!current) return current;
        return {
          ...current,
          channels: current.channels.map((entry) =>
            entry.channelId === payload.channelId
              ? { ...entry, currentAlbumId: payload.currentAlbumId, currentSlideId: payload.currentSlideId, isPaused: payload.isPaused }
              : entry
          )
        };
      });
    });

    connection
      .start()
      .then(() => connection.invoke<AdminSnapshot>('JoinAdmin'))
      .then((initial) => {
        if (!cancelled) setSnapshot(initial);
      });

    return () => {
      cancelled = true;
      connection.stop();
    };
  }, [factory]);

  return {
    snapshot,
    requestNext: async (channelId) => { await connectionRef.current?.invoke('RequestNextSlide', channelId); },
    requestPrevious: async (channelId) => { await connectionRef.current?.invoke('RequestPreviousSlide', channelId); },
    requestJump: async (channelId, slideId) => { await connectionRef.current?.invoke('RequestJumpToSlide', channelId, slideId); },
    requestTogglePause: async (channelId) => { await connectionRef.current?.invoke('RequestTogglePause', channelId); },
    requestSwitchAlbum: async (channelId, albumId) => { await connectionRef.current?.invoke('RequestSwitchAlbum', channelId, albumId); },
    requestLink: async (channelIds) => connectionRef.current!.invoke<LinkResult>('RequestLinkChannels', channelIds),
    requestUnlink: async (channelId) => { await connectionRef.current?.invoke('RequestUnlinkChannel', channelId); }
  };
}
```

- [ ] **Step 4: Run the hook tests to verify they pass**

Run: `cd SynologySlideshow.Web && npm test -- useAdminConnection`
Expected: PASS (6 tests).

- [ ] **Step 5: Write the failing `AdminPage` tests**

Replace `SynologySlideshow.Web/src/components/AdminPage.test.tsx` in full:

```tsx
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { AdminPage } from './AdminPage';
import * as adminHook from '../hooks/useAdminConnection';
import * as api from '../services/api';
import { AdminSnapshot } from '../types';

vi.mock('../hooks/useAdminConnection');
vi.mock('../services/api');

describe('AdminPage', () => {
  const baseSnapshot: AdminSnapshot = {
    anonymousCount: 2,
    channels: [
      { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, viewerCount: 3, linkedChannelIds: [] },
      { channelId: 2, name: 'living-room', currentAlbumId: 5, currentSlideId: 10, isPaused: false, viewerCount: 1, linkedChannelIds: [] }
    ]
  };

  const mockHook = (overrides: Partial<ReturnType<typeof adminHook.useAdminConnection>> = {}) =>
    vi.mocked(adminHook.useAdminConnection).mockReturnValue({
      snapshot: baseSnapshot,
      requestNext: vi.fn(),
      requestPrevious: vi.fn(),
      requestJump: vi.fn(),
      requestTogglePause: vi.fn(),
      requestSwitchAlbum: vi.fn(),
      requestLink: vi.fn().mockResolvedValue({ success: true, error: null }),
      requestUnlink: vi.fn(),
      ...overrides
    });

  beforeEach(() => {
    vi.mocked(api.getAlbums).mockResolvedValue({ data: [{ id: 5, name: 'Holiday', thumbnail: '' }] } as any);
    vi.mocked(api.createChannel).mockResolvedValue({ data: { id: 3, name: 'bedroom' } } as any);
    vi.mocked(api.deleteChannel).mockResolvedValue({} as any);
  });

  it('renders the anonymous count and each channel from the snapshot', () => {
    mockHook();

    render(<AdminPage />);

    expect(screen.getByText('Anonymous: 2 connected')).toBeInTheDocument();
    expect(screen.getByText('kitchen')).toBeInTheDocument();
    expect(screen.getAllByText('Playing').length).toBe(2);
  });

  it('creates a channel through the form', async () => {
    mockHook();

    render(<AdminPage />);

    fireEvent.change(screen.getByPlaceholderText('New channel name'), { target: { value: 'bedroom' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create' }));

    await waitFor(() => expect(api.createChannel).toHaveBeenCalledWith('bedroom'));
  });

  it('requests a pause toggle for the right channel', () => {
    const requestTogglePause = vi.fn();
    mockHook({ requestTogglePause });

    render(<AdminPage />);

    fireEvent.click(screen.getAllByRole('button', { name: 'Pause' })[0]);

    expect(requestTogglePause).toHaveBeenCalledWith(1);
  });

  it('deletes a channel through its row button', async () => {
    mockHook();

    render(<AdminPage />);

    fireEvent.click(screen.getAllByRole('button', { name: 'Delete' })[0]);

    await waitFor(() => expect(api.deleteChannel).toHaveBeenCalledWith(1));
  });

  it('shows a loading state before the first snapshot arrives', () => {
    mockHook({ snapshot: null } as any);

    render(<AdminPage />);

    expect(screen.getByText('Loading…')).toBeInTheDocument();
  });

  it('links the selected channels once two or more are checked', async () => {
    const requestLink = vi.fn().mockResolvedValue({ success: true, error: null });
    mockHook({ requestLink });

    render(<AdminPage />);

    const checkboxes = screen.getAllByRole('checkbox');
    fireEvent.click(checkboxes[0]);
    fireEvent.click(checkboxes[1]);
    fireEvent.click(screen.getByRole('button', { name: 'Link selected' }));

    await waitFor(() => expect(requestLink).toHaveBeenCalledWith([1, 2]));
  });

  it('disables the link button until at least two channels are selected', () => {
    mockHook();

    render(<AdminPage />);

    expect(screen.getByRole('button', { name: 'Link selected' })).toBeDisabled();

    fireEvent.click(screen.getAllByRole('checkbox')[0]);

    expect(screen.getByRole('button', { name: 'Link selected' })).toBeDisabled();
  });

  it('shows a link error returned by the server instead of clearing the selection', async () => {
    const requestLink = vi.fn().mockResolvedValue({ success: false, error: 'Channel 2 is already in a sync group.' });
    mockHook({ requestLink });

    render(<AdminPage />);

    fireEvent.click(screen.getAllByRole('checkbox')[0]);
    fireEvent.click(screen.getAllByRole('checkbox')[1]);
    fireEvent.click(screen.getByRole('button', { name: 'Link selected' }));

    expect(await screen.findByText('Channel 2 is already in a sync group.')).toBeInTheDocument();
  });

  it('unlinks a channel through its row button', () => {
    const requestUnlink = vi.fn();
    const linkedSnapshot: AdminSnapshot = {
      anonymousCount: 0,
      channels: [
        { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, viewerCount: 1, linkedChannelIds: [2] },
        { channelId: 2, name: 'living-room', currentAlbumId: 5, currentSlideId: 10, isPaused: false, viewerCount: 1, linkedChannelIds: [1] }
      ]
    };
    mockHook({ snapshot: linkedSnapshot, requestUnlink } as any);

    render(<AdminPage />);

    fireEvent.click(screen.getAllByRole('button', { name: 'Unlink' })[0]);

    expect(requestUnlink).toHaveBeenCalledWith(1);
  });
});
```

- [ ] **Step 6: Run the tests to verify they fail**

Run: `cd SynologySlideshow.Web && npm test -- AdminPage`
Expected: FAIL — `AdminPage` has no selection/link/unlink UI yet, and existing tests fail because `linkedChannelIds` is now a required field wherever a snapshot object is constructed.

- [ ] **Step 7: Implement the link/unlink UI**

Replace `SynologySlideshow.Web/src/components/AdminPage.tsx` in full:

```tsx
import React, { useEffect, useState } from 'react';
import { useAdminConnection } from '../hooks/useAdminConnection';
import { createChannel, deleteChannel, getAlbums, getAlbumSlides } from '../services/api';
import { Album, Slide } from '../types';

export function AdminPage() {
  const {
    snapshot,
    requestNext,
    requestPrevious,
    requestJump,
    requestTogglePause,
    requestSwitchAlbum,
    requestLink,
    requestUnlink
  } = useAdminConnection();
  const [albums, setAlbums] = useState<Album[]>([]);
  const [newChannelName, setNewChannelName] = useState('');
  const [createError, setCreateError] = useState<string | null>(null);
  const [slidesByAlbum, setSlidesByAlbum] = useState<Record<number, Slide[]>>({});
  const [expandedChannelId, setExpandedChannelId] = useState<number | null>(null);
  const [selectedForLink, setSelectedForLink] = useState<Set<number>>(new Set());
  const [linkError, setLinkError] = useState<string | null>(null);

  useEffect(() => {
    getAlbums().then((response) => setAlbums(response.data));
  }, []);

  const albumName = (albumId: number | null) => albums.find((a) => a.id === albumId)?.name ?? '(no album)';
  const channelName = (channelId: number) => snapshot?.channels.find((c) => c.channelId === channelId)?.name ?? `#${channelId}`;

  const handleCreate = async (event: React.FormEvent) => {
    event.preventDefault();
    setCreateError(null);
    const name = newChannelName.trim();
    try {
      await createChannel(name);
      setNewChannelName('');
    } catch {
      setCreateError(`Could not create '${name}' — the name may already be in use or reserved.`);
    }
  };

  const loadSlidesFor = async (albumId: number) => {
    if (slidesByAlbum[albumId]) return;
    const response = await getAlbumSlides(albumId);
    setSlidesByAlbum((current) => ({ ...current, [albumId]: response.data }));
  };

  const toggleExpanded = async (channelId: number, albumId: number | null) => {
    if (expandedChannelId === channelId) {
      setExpandedChannelId(null);
      return;
    }
    if (albumId != null) {
      await loadSlidesFor(albumId);
    }
    setExpandedChannelId(channelId);
  };

  const toggleSelectedForLink = (channelId: number) => {
    setSelectedForLink((current) => {
      const next = new Set(current);
      if (next.has(channelId)) {
        next.delete(channelId);
      } else {
        next.add(channelId);
      }
      return next;
    });
  };

  const handleLink = async () => {
    setLinkError(null);
    const result = await requestLink(Array.from(selectedForLink));
    if (!result.success) {
      setLinkError(result.error ?? 'Could not link the selected channels.');
      return;
    }
    setSelectedForLink(new Set());
  };

  if (!snapshot) {
    return <p>Loading…</p>;
  }

  return (
    <div className="admin-page">
      <h1>Channels</h1>
      <p>Anonymous: {snapshot.anonymousCount} connected</p>

      <form onSubmit={handleCreate}>
        <input
          value={newChannelName}
          onChange={(e) => setNewChannelName(e.target.value)}
          placeholder="New channel name"
        />
        <button type="submit" disabled={newChannelName.trim().length === 0}>
          Create
        </button>
        {createError && <span className="admin-error">{createError}</span>}
      </form>

      <div className="admin-link-bar">
        <button onClick={handleLink} disabled={selectedForLink.size < 2}>
          Link selected
        </button>
        {linkError && <span className="admin-error">{linkError}</span>}
      </div>

      <table className="admin-channel-table">
        <thead>
          <tr>
            <th></th>
            <th>Name</th>
            <th>Viewers</th>
            <th>Status</th>
            <th>Album</th>
            <th>Linked with</th>
            <th>Controls</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {snapshot.channels.map((channel) => (
            <React.Fragment key={channel.channelId}>
              <tr>
                <td>
                  <input
                    type="checkbox"
                    checked={selectedForLink.has(channel.channelId)}
                    onChange={() => toggleSelectedForLink(channel.channelId)}
                  />
                </td>
                <td>{channel.name}</td>
                <td>{channel.viewerCount}</td>
                <td>{channel.isPaused ? 'Paused' : 'Playing'}</td>
                <td>
                  <select
                    value={channel.currentAlbumId ?? ''}
                    onChange={(e) => requestSwitchAlbum(channel.channelId, Number(e.target.value))}
                  >
                    <option value="" disabled>
                      {albumName(channel.currentAlbumId)}
                    </option>
                    {albums.map((album) => (
                      <option key={album.id} value={album.id}>
                        {album.name}
                      </option>
                    ))}
                  </select>
                </td>
                <td>
                  {channel.linkedChannelIds.length === 0 ? (
                    '—'
                  ) : (
                    <>
                      {channel.linkedChannelIds.map(channelName).join(', ')}{' '}
                      <button onClick={() => requestUnlink(channel.channelId)}>Unlink</button>
                    </>
                  )}
                </td>
                <td>
                  <button onClick={() => requestTogglePause(channel.channelId)}>
                    {channel.isPaused ? 'Play' : 'Pause'}
                  </button>
                  <button onClick={() => requestPrevious(channel.channelId)}>Previous</button>
                  <button onClick={() => requestNext(channel.channelId)}>Next</button>
                  <button onClick={() => toggleExpanded(channel.channelId, channel.currentAlbumId)}>
                    {expandedChannelId === channel.channelId ? 'Hide slides' : 'Jump to slide…'}
                  </button>
                </td>
                <td>
                  <button onClick={() => deleteChannel(channel.channelId)}>Delete</button>
                </td>
              </tr>
              {expandedChannelId === channel.channelId && channel.currentAlbumId != null && (
                <tr>
                  <td colSpan={8}>
                    <ul className="admin-slide-grid">
                      {(slidesByAlbum[channel.currentAlbumId] ?? []).map((slide) => (
                        <li key={slide.id}>
                          <button
                            className={slide.id === channel.currentSlideId ? 'selected' : ''}
                            style={{ backgroundImage: `url('${slide.uri}')` }}
                            onClick={() => requestJump(channel.channelId, slide.id)}
                          />
                        </li>
                      ))}
                    </ul>
                  </td>
                </tr>
              )}
            </React.Fragment>
          ))}
        </tbody>
      </table>
    </div>
  );
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `cd SynologySlideshow.Web && npm test -- AdminPage`
Expected: PASS (9 tests).

- [ ] **Step 9: Run the full frontend test suite and the production build**

Run: `cd SynologySlideshow.Web && npm test && npm run build`
Expected: all tests PASS; build succeeds.

- [ ] **Step 10: Commit**

```bash
git add SynologySlideshow.Web/src/types/index.ts SynologySlideshow.Web/src/hooks/useAdminConnection.ts SynologySlideshow.Web/src/hooks/useAdminConnection.test.ts SynologySlideshow.Web/src/components/AdminPage.tsx SynologySlideshow.Web/src/components/AdminPage.test.tsx
git commit -m "feat: add link/unlink controls to the admin page"
```
