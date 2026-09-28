# Admin Interface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `/admin` page that lists every Channel with its live state (viewer count, current album, current slide, paused/playing), shows an aggregate anonymous-connection count, and lets the admin create/delete Channels and control any of them (pause/unpause, switch album, step or jump to a specific slide).

**Architecture:** A `PresenceTracker` singleton counts live SignalR connections in memory (total, and per-channel once joined); an `AdminSnapshotService` composes that with the persisted Channel list and `ChannelPlaybackService`'s live state into one `AdminSnapshot` payload, served both as a REST snapshot (for the page's first paint) and pushed live over the hub (`PresenceChanged` on connect/disconnect/join/leave, `ChannelStateChanged` reused for playback changes) to an `"admin"` SignalR group. The frontend gets a `useAdminConnection` hook mirroring `useChannelConnection`'s shape, and one `AdminPage` component. The existing anonymous `Home.tsx` gets one small addition: it opens a bare, otherwise-unused hub connection so it's counted in the anonymous total.

**Tech Stack:** Same as the core-infrastructure and client-viewing plans — no new packages.

**Spec:** `docs/superpowers/plans/2026-09-28-channel-feature-spec.md`

## Global Constraints

- `/admin` has no authentication, matching the rest of the app's LAN-trust model.
- Presence counts are purely in-memory (`PresenceTracker`) and reset on restart along with every live connection anyway — nothing about presence is persisted, only Channel rows themselves are.
- The anonymous experience must keep working exactly as it does today even if its presence connection never manages to connect (e.g. offline) — that connection is best-effort and its failure must be swallowed, never surfaced to the person looking at the slideshow.

## Review Focus

- A viewer disconnecting without ever calling `LeaveChannel` (closing a tab, losing wifi) must still be removed from that channel's viewer count and from the anonymous total — presence must not leak upward forever.
- An admin's own `AdminPage` load happening before any channel has ever been created must render an empty, non-broken table plus the anonymous count, not crash on an empty snapshot.
- Two different browsers viewing the admin page must both see a viewer-count or paused-state change made from a third connection, live, without reloading — that's the entire point of this plan.
- Deleting a channel that's currently being viewed must remove it from the admin table and (per the core-infrastructure plan's existing behavior) leave any of its viewers on a now-nonexistent channel — this plan doesn't need to fix that (Plan 2 already redirects a viewer whose channel disappears out from under it), but the admin table itself must reflect the deletion immediately, not show a stale row.
- Creating a channel with a name that's a duplicate or reserved (`admin`) must surface a clear, visible error in the admin UI, not fail silently.

---

## Task 1: Presence tracking, the admin snapshot, and its endpoints

**Files:**
- Create: `SynologySlideshow.Api/Services/PresenceTracker.cs`
- Create: `SynologySlideshow.Api/Realtime/AdminSnapshot.cs`
- Create: `SynologySlideshow.Api/Services/AdminSnapshotService.cs`
- Create: `SynologySlideshow.Api/Controllers/AdminController.cs`
- Create: `SynologySlideshow.Api.Tests/Services/PresenceTrackerTests.cs`
- Create: `SynologySlideshow.Api.Tests/Realtime/AdminPresenceTests.cs`
- Modify: `SynologySlideshow.Api/Realtime/SlideshowHub.cs`
- Modify: `SynologySlideshow.Api/Services/ChannelPlaybackService.cs`
- Modify: `SynologySlideshow.Api/Program.cs`

**Interfaces:**
- Consumes: `SlideshowDbContext`, `Channel`, `ChannelPlaybackService.GetStateAsync` (core-infrastructure plan).
- Produces: `SynologySlideshow.Api.Services.PresenceTracker` with `OnConnected()`, `OnDisconnected(string connectionId)`, `OnJoinedChannel(string connectionId, int channelId)`, `OnLeftChannel(string connectionId, int channelId)`, `GetViewerCount(int channelId): int`, `GetAnonymousCount(): int`; `SynologySlideshow.Api.Realtime.AdminChannelEntry(int ChannelId, string Name, int? CurrentAlbumId, int? CurrentSlideId, bool IsPaused, int ViewerCount)` and `AdminSnapshot(int AnonymousCount, AdminChannelEntry[] Channels)`; `AdminSnapshotService.BuildAsync(): Task<AdminSnapshot>`; `GET /api/admin/snapshot`; hub method `JoinAdmin(): Task<AdminSnapshot>` and hub event `"PresenceChanged"` carrying an `AdminSnapshot` — the exact contract Task 3 (frontend `AdminPage`) consumes.

- [ ] **Step 1: Write the failing `PresenceTracker` unit tests**

Create `SynologySlideshow.Api.Tests/Services/PresenceTrackerTests.cs`:

```csharp
using SynologySlideshow.Api.Services;
using Xunit;

namespace SynologySlideshow.Api.Tests.Services;

public class PresenceTrackerTests
{
    [Fact]
    public void AnonymousCountEqualsTotalConnectionsMinusJoinedOnes()
    {
        var tracker = new PresenceTracker();

        tracker.OnConnected(); // connection A, stays anonymous
        tracker.OnConnected(); // connection B, will join a channel
        tracker.OnJoinedChannel("B", channelId: 1);

        Assert.Equal(1, tracker.GetAnonymousCount());
        Assert.Equal(1, tracker.GetViewerCount(1));
    }

    [Fact]
    public void LeavingAChannelReturnsToAnonymous()
    {
        var tracker = new PresenceTracker();
        tracker.OnConnected();
        tracker.OnJoinedChannel("A", channelId: 1);

        tracker.OnLeftChannel("A", channelId: 1);

        Assert.Equal(1, tracker.GetAnonymousCount());
        Assert.Equal(0, tracker.GetViewerCount(1));
    }

    [Fact]
    public void DisconnectingWhileJoinedRemovesFromTheChannelToo()
    {
        var tracker = new PresenceTracker();
        tracker.OnConnected();
        tracker.OnJoinedChannel("A", channelId: 1);

        tracker.OnDisconnected("A");

        Assert.Equal(0, tracker.GetAnonymousCount());
        Assert.Equal(0, tracker.GetViewerCount(1));
    }

    [Fact]
    public void MultipleViewersOfTheSameChannelAreAllCounted()
    {
        var tracker = new PresenceTracker();
        tracker.OnConnected();
        tracker.OnConnected();
        tracker.OnJoinedChannel("A", channelId: 1);
        tracker.OnJoinedChannel("B", channelId: 1);

        Assert.Equal(2, tracker.GetViewerCount(1));
        Assert.Equal(0, tracker.GetAnonymousCount());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SynologySlideshow.Api.Tests --filter PresenceTrackerTests`
Expected: build FAILS — `PresenceTracker` doesn't exist yet.

- [ ] **Step 3: Implement `PresenceTracker`**

Create `SynologySlideshow.Api/Services/PresenceTracker.cs`:

```csharp
using System.Collections.Concurrent;

namespace SynologySlideshow.Api.Services;

public class PresenceTracker
{
    private int _totalConnections;
    private readonly ConcurrentDictionary<string, int> _connectionChannel = new();
    private readonly ConcurrentDictionary<int, int> _viewerCounts = new();

    public void OnConnected() => Interlocked.Increment(ref _totalConnections);

    public void OnDisconnected(string connectionId)
    {
        Interlocked.Decrement(ref _totalConnections);
        if (_connectionChannel.TryRemove(connectionId, out var channelId))
        {
            _viewerCounts.AddOrUpdate(channelId, 0, (_, count) => Math.Max(0, count - 1));
        }
    }

    public void OnJoinedChannel(string connectionId, int channelId)
    {
        _connectionChannel[connectionId] = channelId;
        _viewerCounts.AddOrUpdate(channelId, 1, (_, count) => count + 1);
    }

    public void OnLeftChannel(string connectionId, int channelId)
    {
        if (_connectionChannel.TryRemove(connectionId, out _))
        {
            _viewerCounts.AddOrUpdate(channelId, 0, (_, count) => Math.Max(0, count - 1));
        }
    }

    public int GetViewerCount(int channelId) => _viewerCounts.TryGetValue(channelId, out var count) ? count : 0;

    public int GetAnonymousCount() => Math.Max(0, _totalConnections - _viewerCounts.Values.Sum());
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SynologySlideshow.Api.Tests --filter PresenceTrackerTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Write the failing integration tests for the snapshot and live pushes**

Create `SynologySlideshow.Api.Tests/Realtime/AdminPresenceTests.cs`:

```csharp
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SynologySlideshow.Api.Controllers;
using SynologySlideshow.Api.Realtime;
using Xunit;

namespace SynologySlideshow.Api.Tests.Realtime;

public class AdminPresenceTests : IClassFixture<SlideshowApiFactory>, IAsyncLifetime
{
    private readonly SlideshowApiFactory _factory;
    private HubConnection _viewer = null!;
    private HubConnection _admin = null!;

    public AdminPresenceTests(SlideshowApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _viewer = BuildConnection();
        _admin = BuildConnection();
        await _viewer.StartAsync();
        await _admin.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _viewer.DisposeAsync();
        await _admin.DisposeAsync();
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
    public async Task JoinAdminReturnsTheChannelWithItsCurrentViewerCount()
    {
        var channel = await CreateChannelAsync("presence-snapshot-test");
        await _viewer.InvokeAsync("JoinChannel", "presence-snapshot-test");

        var snapshot = await _admin.InvokeAsync<AdminSnapshot>("JoinAdmin");

        Assert.Contains(snapshot.Channels, c => c.ChannelId == channel.Id && c.ViewerCount == 1);
    }

    [Fact]
    public async Task AdminReceivesAPresenceUpdateWhenAViewerJoins()
    {
        var channel = await CreateChannelAsync("presence-push-test");
        await _admin.InvokeAsync("JoinAdmin");

        var updates = new List<AdminSnapshot>();
        _admin.On<AdminSnapshot>("PresenceChanged", updates.Add);

        await _viewer.InvokeAsync("JoinChannel", "presence-push-test");

        await WaitUntilAsync(() => updates.Any(s => s.Channels.Any(c => c.ChannelId == channel.Id && c.ViewerCount == 1)), TimeSpan.FromSeconds(5));
        Assert.Contains(updates, s => s.Channels.Any(c => c.ChannelId == channel.Id && c.ViewerCount == 1));
    }

    [Fact]
    public async Task AdminReceivesChannelStateChangedWhenAViewerControlsIt()
    {
        var channel = await CreateChannelAsync("presence-state-test");
        await _admin.InvokeAsync("JoinAdmin");
        await _viewer.InvokeAsync("JoinChannel", "presence-state-test");

        var received = new List<ChannelStateDto>();
        _admin.On<ChannelStateDto>("ChannelStateChanged", received.Add);

        await _viewer.InvokeAsync("RequestTogglePause", channel.Id);

        await WaitUntilAsync(() => received.Any(s => s.ChannelId == channel.Id && s.IsPaused), TimeSpan.FromSeconds(5));
        Assert.Contains(received, s => s.ChannelId == channel.Id && s.IsPaused);
    }

    [Fact]
    public async Task DisconnectingRemovesTheViewerFromTheSnapshot()
    {
        var channel = await CreateChannelAsync("presence-disconnect-test");
        await _viewer.InvokeAsync("JoinChannel", "presence-disconnect-test");

        await _viewer.DisposeAsync();

        var snapshot = await _admin.InvokeAsync<AdminSnapshot>("JoinAdmin");
        Assert.Contains(snapshot.Channels, c => c.ChannelId == channel.Id && c.ViewerCount == 0);
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

Run: `dotnet test SynologySlideshow.Api.Tests --filter AdminPresenceTests`
Expected: build FAILS — `AdminSnapshot`, `AdminSnapshotService`, `JoinAdmin`, and the presence-broadcast wiring don't exist yet.

- [ ] **Step 7: Implement the snapshot type, service, and REST endpoint**

Create `SynologySlideshow.Api/Realtime/AdminSnapshot.cs`:

```csharp
namespace SynologySlideshow.Api.Realtime;

public record AdminChannelEntry(int ChannelId, string Name, int? CurrentAlbumId, int? CurrentSlideId, bool IsPaused, int ViewerCount);

public record AdminSnapshot(int AnonymousCount, AdminChannelEntry[] Channels);
```

Create `SynologySlideshow.Api/Services/AdminSnapshotService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Realtime;

namespace SynologySlideshow.Api.Services;

public class AdminSnapshotService
{
    private readonly SlideshowDbContext _db;
    private readonly ChannelPlaybackService _playback;
    private readonly PresenceTracker _presence;

    public AdminSnapshotService(SlideshowDbContext db, ChannelPlaybackService playback, PresenceTracker presence)
    {
        _db = db;
        _playback = playback;
        _presence = presence;
    }

    public async Task<AdminSnapshot> BuildAsync()
    {
        var channels = await _db.Channels.OrderBy(c => c.Name).ToListAsync();
        var entries = new List<AdminChannelEntry>();
        foreach (var channel in channels)
        {
            var state = await _playback.GetStateAsync(channel.Id);
            entries.Add(new AdminChannelEntry(
                channel.Id,
                channel.Name,
                state.CurrentAlbumId,
                state.CurrentSlideId,
                state.IsPaused,
                _presence.GetViewerCount(channel.Id)));
        }
        return new AdminSnapshot(_presence.GetAnonymousCount(), entries.ToArray());
    }
}
```

Create `SynologySlideshow.Api/Controllers/AdminController.cs`:

```csharp
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
```

- [ ] **Step 8: Wire presence tracking and broadcasts into the hub**

Replace `SynologySlideshow.Api/Realtime/SlideshowHub.cs`:

```csharp
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;
using SynologySlideshow.Api.Services;

namespace SynologySlideshow.Api.Realtime;

public class SlideshowHub : Hub
{
    public const string AdminGroupName = "admin";

    private readonly SlideshowDbContext _db;
    private readonly ChannelPlaybackService _playback;
    private readonly PresenceTracker _presence;
    private readonly AdminSnapshotService _snapshotService;

    public SlideshowHub(SlideshowDbContext db, ChannelPlaybackService playback, PresenceTracker presence, AdminSnapshotService snapshotService)
    {
        _db = db;
        _playback = playback;
        _presence = presence;
        _snapshotService = snapshotService;
    }

    public static string GroupName(int channelId) => $"channel:{channelId}";

    public override async Task OnConnectedAsync()
    {
        _presence.OnConnected();
        await BroadcastPresenceAsync();
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _presence.OnDisconnected(Context.ConnectionId);
        await BroadcastPresenceAsync();
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<AdminSnapshot> JoinAdmin()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroupName);
        return await _snapshotService.BuildAsync();
    }

    public async Task<ChannelStateDto?> JoinChannel(string channelName)
    {
        var channel = await _db.Channels.SingleOrDefaultAsync(c => c.Name == channelName);
        if (channel == null) return null;

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(channel.Id));
        _presence.OnJoinedChannel(Context.ConnectionId, channel.Id);
        await BroadcastPresenceAsync();
        return await _playback.GetStateAsync(channel.Id);
    }

    public async Task LeaveChannel(int channelId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(channelId));
        _presence.OnLeftChannel(Context.ConnectionId, channelId);
        await BroadcastPresenceAsync();
    }

    public Task<ChannelStateDto> RequestNextSlide(int channelId) => _playback.AdvanceAsync(channelId, 1);

    public Task<ChannelStateDto> RequestPreviousSlide(int channelId) => _playback.AdvanceAsync(channelId, -1);

    public Task<ChannelStateDto> RequestJumpToSlide(int channelId, int slideId) => _playback.JumpAsync(channelId, slideId);

    public Task<ChannelStateDto> RequestTogglePause(int channelId) => _playback.TogglePauseAsync(channelId);

    public Task<ChannelStateDto> RequestSwitchAlbum(int channelId, int albumId) => _playback.SetAlbumAsync(channelId, albumId);

    private async Task BroadcastPresenceAsync()
    {
        var snapshot = await _snapshotService.BuildAsync();
        await Clients.Group(AdminGroupName).SendAsync("PresenceChanged", snapshot);
    }
}
```

In `SynologySlideshow.Api/Services/ChannelPlaybackService.cs`, modify `BuildAndBroadcastAsync` so admins watching also see playback changes live:

```csharp
    private async Task<ChannelStateDto> BuildAndBroadcastAsync(Channel channel, ChannelRuntimeState runtime)
    {
        var dto = ToDto(channel, runtime);
        await _hubContext.Clients.Group(SlideshowHub.GroupName(channel.Id)).SendAsync("ChannelStateChanged", dto);
        await _hubContext.Clients.Group(SlideshowHub.AdminGroupName).SendAsync("ChannelStateChanged", dto);
        return dto;
    }
```

In `SynologySlideshow.Api/Program.cs`, add these two registrations right after the existing `builder.Services.AddHostedService<ChannelTimerStartup>();` line:

```csharp
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddScoped<AdminSnapshotService>();
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test SynologySlideshow.Api.Tests --filter AdminPresenceTests`
Expected: PASS (4 tests).

- [ ] **Step 10: Run the full backend test suite**

Run: `dotnet test SynologySlideshow.Api.Tests`
Expected: PASS (every test from this and both earlier plans).

- [ ] **Step 11: Commit**

```bash
git add SynologySlideshow.Api/Services/PresenceTracker.cs SynologySlideshow.Api/Realtime/AdminSnapshot.cs SynologySlideshow.Api/Services/AdminSnapshotService.cs SynologySlideshow.Api/Controllers/AdminController.cs SynologySlideshow.Api/Realtime/SlideshowHub.cs SynologySlideshow.Api/Services/ChannelPlaybackService.cs SynologySlideshow.Api/Program.cs SynologySlideshow.Api.Tests
git commit -m "feat: add presence tracking and the admin snapshot"
```

---

## Task 2: `useAdminConnection` hook

**Files:**
- Modify: `SynologySlideshow.Web/src/types/index.ts`
- Create: `SynologySlideshow.Web/src/hooks/useAdminConnection.ts`
- Create: `SynologySlideshow.Web/src/hooks/useAdminConnection.test.ts`

**Interfaces:**
- Consumes: `HubConnectionLike`, `ConnectionFactory` (client-viewing plan), `ChannelState` (client-viewing plan).
- Produces: `AdminChannelEntry { channelId: number; name: string; currentAlbumId: number | null; currentSlideId: number | null; isPaused: boolean; viewerCount: number }`, `AdminSnapshot { anonymousCount: number; channels: AdminChannelEntry[] }` (in `types/index.ts`); `useAdminConnection(factory?: ConnectionFactory): { snapshot: AdminSnapshot | null; requestNext(channelId): Promise<void>; requestPrevious(channelId): Promise<void>; requestJump(channelId, slideId): Promise<void>; requestTogglePause(channelId): Promise<void>; requestSwitchAlbum(channelId, albumId): Promise<void> }` — the exact shape Task 3's `AdminPage` consumes.

- [ ] **Step 1: Write the failing tests**

Add to `SynologySlideshow.Web/src/types/index.ts`:

```ts
export interface AdminChannelEntry {
  channelId: number;
  name: string;
  currentAlbumId: number | null;
  currentSlideId: number | null;
  isPaused: boolean;
  viewerCount: number;
}

export interface AdminSnapshot {
  anonymousCount: number;
  channels: AdminChannelEntry[];
}
```

Create `SynologySlideshow.Web/src/hooks/useAdminConnection.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { useAdminConnection } from './useAdminConnection';
import { HubConnectionLike } from './useChannelConnection';
import { AdminSnapshot, ChannelState } from '../types';

class FakeConnection implements HubConnectionLike {
  handlers = new Map<string, (...args: any[]) => void>();
  invokeCalls: [string, any[]][] = [];
  snapshot: AdminSnapshot = { anonymousCount: 0, channels: [] };

  on(methodName: string, callback: (...args: any[]) => void): void {
    this.handlers.set(methodName, callback);
  }

  async invoke<T = void>(methodName: string, ...args: any[]): Promise<T> {
    this.invokeCalls.push([methodName, args]);
    if (methodName === 'JoinAdmin') {
      return this.snapshot as unknown as T;
    }
    return undefined as unknown as T;
  }

  async start(): Promise<void> {}
  async stop(): Promise<void> {}

  emit(methodName: string, payload: unknown) {
    this.handlers.get(methodName)?.(payload);
  }
}

describe('useAdminConnection', () => {
  it('joins the admin group on start and exposes the initial snapshot', async () => {
    const fake = new FakeConnection();
    fake.snapshot = { anonymousCount: 3, channels: [] };

    const { result } = renderHook(() => useAdminConnection(() => fake));

    await waitFor(() => expect(result.current.snapshot).not.toBeNull());
    expect(result.current.snapshot?.anonymousCount).toBe(3);
  });

  it('replaces the snapshot when PresenceChanged is pushed', async () => {
    const fake = new FakeConnection();
    fake.snapshot = { anonymousCount: 1, channels: [] };

    const { result } = renderHook(() => useAdminConnection(() => fake));
    await waitFor(() => expect(result.current.snapshot).not.toBeNull());

    act(() => {
      fake.emit('PresenceChanged', { anonymousCount: 5, channels: [] });
    });

    await waitFor(() => expect(result.current.snapshot?.anonymousCount).toBe(5));
  });

  it('merges a ChannelStateChanged push into the matching channel entry without touching its viewer count', async () => {
    const fake = new FakeConnection();
    fake.snapshot = {
      anonymousCount: 0,
      channels: [{ channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, viewerCount: 1 }]
    };

    const { result } = renderHook(() => useAdminConnection(() => fake));
    await waitFor(() => expect(result.current.snapshot).not.toBeNull());

    const pushed: ChannelState = { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 20, isPaused: true };
    act(() => {
      fake.emit('ChannelStateChanged', pushed);
    });

    await waitFor(() => expect(result.current.snapshot?.channels[0].currentSlideId).toBe(20));
    expect(result.current.snapshot?.channels[0].viewerCount).toBe(1);
  });

  it('sends the channel id when requesting a pause toggle', async () => {
    const fake = new FakeConnection();
    fake.snapshot = { anonymousCount: 0, channels: [] };

    const { result } = renderHook(() => useAdminConnection(() => fake));
    await waitFor(() => expect(result.current.snapshot).not.toBeNull());

    await act(async () => {
      await result.current.requestTogglePause(9);
    });

    expect(fake.invokeCalls).toContainEqual(['RequestTogglePause', [9]]);
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd SynologySlideshow.Web && npm test -- useAdminConnection`
Expected: FAIL — `./useAdminConnection` doesn't exist yet.

- [ ] **Step 3: Implement the hook**

Create `SynologySlideshow.Web/src/hooks/useAdminConnection.ts`:

```ts
import { useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { AdminSnapshot, ChannelState } from '../types';
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
    requestSwitchAlbum: async (channelId, albumId) => { await connectionRef.current?.invoke('RequestSwitchAlbum', channelId, albumId); }
  };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd SynologySlideshow.Web && npm test -- useAdminConnection`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add SynologySlideshow.Web/src/types/index.ts SynologySlideshow.Web/src/hooks/useAdminConnection.ts SynologySlideshow.Web/src/hooks/useAdminConnection.test.ts
git commit -m "feat: add admin connection hook"
```

---

## Task 3: `AdminPage` — list, create/delete, and controls

**Files:**
- Modify: `SynologySlideshow.Web/src/services/api.ts`
- Create: `SynologySlideshow.Web/src/components/AdminPage.tsx`
- Create: `SynologySlideshow.Web/src/components/AdminPage.test.tsx`
- Modify: `SynologySlideshow.Web/src/App.tsx`

**Interfaces:**
- Consumes: `useAdminConnection` (Task 2), `getAlbums`/`getAlbumSlides` (existing), `AdminSnapshot`/`AdminChannelEntry` (Task 2).
- Produces: `createChannel(name: string)`, `deleteChannel(id: number)`, `getAdminSnapshot()` in `services/api.ts`; route `/admin` → `AdminPage`.

- [ ] **Step 1: Write the failing tests**

Create `SynologySlideshow.Web/src/components/AdminPage.test.tsx`:

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
      { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, viewerCount: 3 }
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
      ...overrides
    });

  beforeEach(() => {
    vi.mocked(api.getAlbums).mockResolvedValue({ data: [{ id: 5, name: 'Holiday', thumbnail: '' }] } as any);
    vi.mocked(api.createChannel).mockResolvedValue({ data: { id: 2, name: 'bedroom' } } as any);
    vi.mocked(api.deleteChannel).mockResolvedValue({} as any);
  });

  it('renders the anonymous count and each channel from the snapshot', () => {
    mockHook();

    render(<AdminPage />);

    expect(screen.getByText('Anonymous: 2 connected')).toBeInTheDocument();
    expect(screen.getByText('kitchen')).toBeInTheDocument();
    expect(screen.getByText('Playing')).toBeInTheDocument();
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

    fireEvent.click(screen.getByRole('button', { name: 'Pause' }));

    expect(requestTogglePause).toHaveBeenCalledWith(1);
  });

  it('deletes a channel through its row button', async () => {
    mockHook();

    render(<AdminPage />);

    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(api.deleteChannel).toHaveBeenCalledWith(1));
  });

  it('shows a loading state before the first snapshot arrives', () => {
    mockHook({ snapshot: null } as any);

    render(<AdminPage />);

    expect(screen.getByText('Loading…')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd SynologySlideshow.Web && npm test -- AdminPage`
Expected: FAIL — `./AdminPage`, `api.createChannel`, `api.deleteChannel` don't exist yet.

- [ ] **Step 3: Add the channel management calls to the API client**

Modify `SynologySlideshow.Web/src/services/api.ts`:

```ts
import axios from 'axios';
import { AdminSnapshot, Album, ChannelSummary, Slide } from '../types';

const api = axios.create({
  baseURL: '/api'
});

export const getAlbums = () => api.get<Album[]>('/albums');

export const getAlbumSlides = (albumId: number) =>
  api.get<Slide[]>(`/albums/${albumId}/slides`);

export const getChannels = () => api.get<ChannelSummary[]>('/channels');

export const createChannel = (name: string) => api.post<ChannelSummary>('/channels', { name });

export const deleteChannel = (id: number) => api.delete(`/channels/${id}`);

export const getAdminSnapshot = () => api.get<AdminSnapshot>('/admin/snapshot');
```

- [ ] **Step 4: Implement `AdminPage`**

Create `SynologySlideshow.Web/src/components/AdminPage.tsx`:

```tsx
import React, { useEffect, useState } from 'react';
import { useAdminConnection } from '../hooks/useAdminConnection';
import { createChannel, deleteChannel, getAlbums, getAlbumSlides } from '../services/api';
import { Album, Slide } from '../types';

export function AdminPage() {
  const { snapshot, requestNext, requestPrevious, requestJump, requestTogglePause, requestSwitchAlbum } = useAdminConnection();
  const [albums, setAlbums] = useState<Album[]>([]);
  const [newChannelName, setNewChannelName] = useState('');
  const [createError, setCreateError] = useState<string | null>(null);
  const [slidesByAlbum, setSlidesByAlbum] = useState<Record<number, Slide[]>>({});
  const [expandedChannelId, setExpandedChannelId] = useState<number | null>(null);

  useEffect(() => {
    getAlbums().then((response) => setAlbums(response.data));
  }, []);

  const albumName = (albumId: number | null) => albums.find((a) => a.id === albumId)?.name ?? '(no album)';

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

      <table className="admin-channel-table">
        <thead>
          <tr>
            <th>Name</th>
            <th>Viewers</th>
            <th>Status</th>
            <th>Album</th>
            <th>Controls</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {snapshot.channels.map((channel) => (
            <React.Fragment key={channel.channelId}>
              <tr>
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
                  <td colSpan={6}>
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

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd SynologySlideshow.Web && npm test -- AdminPage`
Expected: PASS (5 tests).

- [ ] **Step 6: Add the `/admin` route**

Modify `SynologySlideshow.Web/src/App.tsx` — add the import and the route (before the `/:channelName` catch-all, so `admin` never risks being interpreted as a channel name; recall the core-infrastructure plan already guarantees a channel can never actually be named `admin`, so this ordering is a belt-and-suspenders readability choice, not a functional requirement):

```tsx
import { AdminPage } from './components/AdminPage';
```

```tsx
      <Routes>
        <Route path="/" element={<RootRoute />} />
        <Route path="/album/:albumId" element={<Home />} />
        <Route path="/admin" element={<AdminPage />} />
        <Route path="/:channelName" element={<ChannelView />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
```

- [ ] **Step 7: Run the full frontend test suite and the production build**

Run: `cd SynologySlideshow.Web && npm test && npm run build`
Expected: all tests PASS; build succeeds.

- [ ] **Step 8: Commit**

```bash
git add SynologySlideshow.Web/src/services/api.ts SynologySlideshow.Web/src/components/AdminPage.tsx SynologySlideshow.Web/src/components/AdminPage.test.tsx SynologySlideshow.Web/src/App.tsx
git commit -m "feat: add admin page with channel list, create/delete, and controls"
```

---

## Task 4: Count the anonymous experience in presence

**Files:**
- Create: `SynologySlideshow.Web/src/hooks/usePresenceConnection.ts`
- Create: `SynologySlideshow.Web/src/hooks/usePresenceConnection.test.ts`
- Modify: `SynologySlideshow.Web/src/components/Home.tsx`

**Interfaces:**
- Produces: `usePresenceConnection(): void` — a side-effect-only hook. Consumed by `Home.tsx` with a single call, no return value used.

- [ ] **Step 1: Write the failing test**

Create `SynologySlideshow.Web/src/hooks/usePresenceConnection.test.ts`:

```ts
import { describe, expect, it, vi } from 'vitest';
import { renderHook } from '@testing-library/react';
import { usePresenceConnection } from './usePresenceConnection';

const { start, stop } = vi.hoisted(() => ({
  start: vi.fn().mockResolvedValue(undefined),
  stop: vi.fn().mockResolvedValue(undefined)
}));

vi.mock('@microsoft/signalr', () => {
  const connection = { start, stop };
  const builder = {
    withUrl: vi.fn().mockReturnThis(),
    withAutomaticReconnect: vi.fn().mockReturnThis(),
    build: vi.fn().mockReturnValue(connection)
  };
  return {
    HubConnectionBuilder: vi.fn(() => builder)
  };
});

describe('usePresenceConnection', () => {
  it('starts a hub connection on mount and stops it on unmount', () => {
    const { unmount } = renderHook(() => usePresenceConnection());

    expect(start).toHaveBeenCalledTimes(1);

    unmount();

    expect(stop).toHaveBeenCalledTimes(1);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd SynologySlideshow.Web && npm test -- usePresenceConnection`
Expected: FAIL — `./usePresenceConnection` doesn't exist yet.

- [ ] **Step 3: Implement it**

Create `SynologySlideshow.Web/src/hooks/usePresenceConnection.ts`:

```ts
import { useEffect } from 'react';
import * as signalR from '@microsoft/signalr';

export function usePresenceConnection(): void {
  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hub/slideshow')
      .withAutomaticReconnect()
      .build();

    connection.start().catch(() => {
      // best-effort presence registration; the anonymous slideshow works regardless of connectivity here
    });

    return () => {
      connection.stop();
    };
  }, []);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd SynologySlideshow.Web && npm test -- usePresenceConnection`
Expected: PASS (1 test).

- [ ] **Step 5: Wire it into `Home.tsx`**

In `SynologySlideshow.Web/src/components/Home.tsx`, add the import:

```tsx
import { usePresenceConnection } from '../hooks/usePresenceConnection';
```

and call it once near the top of the `Home` function body, alongside the other hook calls (e.g. right after `const isTabVisible = useTabVisibility();`):

```tsx
  usePresenceConnection();
```

- [ ] **Step 6: Run the full frontend test suite and the production build**

Run: `cd SynologySlideshow.Web && npm test && npm run build`
Expected: all tests PASS; build succeeds.

- [ ] **Step 7: Commit**

```bash
git add SynologySlideshow.Web/src/hooks/usePresenceConnection.ts SynologySlideshow.Web/src/hooks/usePresenceConnection.test.ts SynologySlideshow.Web/src/components/Home.tsx
git commit -m "feat: count the anonymous slideshow in admin presence"
```
