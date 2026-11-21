# Channel Client Viewing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a browser render a Channel by visiting `/{channel-name}` — joining it over SignalR, displaying whatever slide/album/pause state the server pushes, and sending control requests for local swipe/keyboard/double-click interactions — while leaving the existing anonymous `/` experience completely untouched.

**Architecture:** A new `useChannelConnection` hook wraps a SignalR `HubConnection` (dependency-injectable for testing) against the hub built in the core-infrastructure plan. A new `ChannelView` (thin container) + `ChannelViewPresentation` (pure, prop-driven) component pair renders the channel, reusing the existing `SlideLayer`/`Clock`/`SwipeArea`/`OverlayMenu`/`AlbumGrid`/`Settings` components as-is. A small `channelMemory` module remembers the last-joined channel name in `localStorage` purely for client-side redirect purposes (never sent to the server), so a kiosk browser that reloads a fixed root URL lands back on its channel. `Home.tsx` gains one small, additive extra: a "join a channel" picker in its existing settings panel.

**Tech Stack:** `@microsoft/signalr` (new), `vitest` + `@testing-library/react` + `@testing-library/jest-dom` + `jsdom` (new — no test framework exists in this project yet).

**Spec:** `docs/superpowers/plans/2026-09-28-channel-feature-spec.md`

## Global Constraints

- The anonymous `/` experience (today's `Home.tsx`) is not modified except for the one additive settings-panel picker in Task 4 — no channel-related network call, and no SignalR connection, happens until a browser actually visits a `/{channel-name}` URL.
- A channel viewer never runs its own slide-advance timer — it only ever renders whatever `ChannelStateChanged` state the server pushes.
- Locally showing the settings/album overlay is decoupled from the shared `isPaused` state: only a *local* double-click, spacebar, or swipe-down may open it. A pause driven by another viewer, by admin, or delivered as part of the initial joined state must never pop it open.
- Cross-fade between the previous and current slide (which the anonymous `Home.tsx` still does) is deliberately not replicated here — a channel view renders only the current slide, no previous-slide fade layer. This is a scope decision for this plan, not an oversight; it can be revisited later.
- Channel names are routed as the single dynamic path segment `/:channelName` — the existing `/album/:albumId` legacy route takes precedence for its own two-segment shape, and (per the core-infrastructure plan) the name `admin` can never exist as a channel, so it can never collide with the future `/admin` route.

## Review Focus

- A remote/externally-driven pause (the server pushing `isPaused: true` because another viewer or admin paused the channel) must not pop the local settings/album overlay open on a screen nobody is interacting with.
- Visiting a channel name that doesn't exist (typo, or a channel deleted while someone was mid-visit) must redirect back to `/` instead of leaving a blank or stuck screen.
- A browser that reloads the fixed root URL after a restart must land back on the channel it last joined, not the anonymous view.
- If the server reports a `currentSlideId` that isn't in the current album's slide list (e.g. the underlying photo was deleted from Synology since), the view must render nothing gracefully, not throw.
- The existing anonymous experience must build and behave identically to today — verified by scope discipline (only one additive change touches `Home.tsx`/`OverlayMenu.tsx`), not by a new test of `Home.tsx` itself.

---

## Task 1: Test infrastructure, SignalR client, and the channel-connection hook

**Files:**
- Modify: `SynologySlideshow.Web/package.json`
- Modify: `SynologySlideshow.Web/vite.config.ts`
- Create: `SynologySlideshow.Web/src/test-setup.ts`
- Modify: `SynologySlideshow.Web/src/types/index.ts`
- Create: `SynologySlideshow.Web/src/hooks/useChannelConnection.ts`
- Create: `SynologySlideshow.Web/src/hooks/useChannelConnection.test.ts`

**Interfaces:**
- Produces: `ChannelState { channelId: number; name: string; currentAlbumId: number | null; currentSlideId: number | null; isPaused: boolean }`, `ChannelSummary { id: number; name: string }` (in `types/index.ts`); `HubConnectionLike` and `ConnectionFactory` types, and `useChannelConnection(channelName: string, factory?: ConnectionFactory): { state: ChannelState | null; notFound: boolean; requestNext(): Promise<void>; requestPrevious(): Promise<void>; requestJump(slideId: number): Promise<void>; requestTogglePause(): Promise<void>; requestSwitchAlbum(albumId: number): Promise<void> }`. This exact return shape is what Task 3's `ChannelView` consumes.

- [ ] **Step 1: Install dependencies and add the test script**

```bash
cd SynologySlideshow.Web
npm install @microsoft/signalr
npm install -D vitest @testing-library/react @testing-library/jest-dom jsdom
```

Update the `scripts` block in `package.json` to add a `test` entry (leave `dev`/`build`/`preview` unchanged):

```json
"scripts": {
  "dev": "vite",
  "build": "tsc && vite build",
  "preview": "vite preview",
  "test": "vitest run"
},
```

- [ ] **Step 2: Wire up the test environment**

Create `SynologySlideshow.Web/src/test-setup.ts`:

```ts
import '@testing-library/jest-dom/vitest';
```

Change the import at the top of `SynologySlideshow.Web/vite.config.ts` from:

```ts
import { defineConfig } from 'vite'
```

to:

```ts
import { defineConfig } from 'vitest/config'
```

Then add a `test` block to the exported config object, alongside the existing `plugins`/`server`/`build` keys:

```ts
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test-setup.ts']
  }
```

- [ ] **Step 3: Write the failing tests**

Add to `SynologySlideshow.Web/src/types/index.ts` (append; leave the existing `Album`/`Slide`/`SwipeDirection`/`ImageZoomMode`/`AppSettings` untouched):

```ts
export interface ChannelState {
  channelId: number;
  name: string;
  currentAlbumId: number | null;
  currentSlideId: number | null;
  isPaused: boolean;
}

export interface ChannelSummary {
  id: number;
  name: string;
}
```

Create `SynologySlideshow.Web/src/hooks/useChannelConnection.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { useChannelConnection, HubConnectionLike } from './useChannelConnection';
import { ChannelState } from '../types';

class FakeConnection implements HubConnectionLike {
  handlers = new Map<string, (...args: any[]) => void>();
  invokeCalls: [string, any[]][] = [];
  joinResult: ChannelState | null = null;

  on(methodName: string, callback: (...args: any[]) => void): void {
    this.handlers.set(methodName, callback);
  }

  async invoke<T = void>(methodName: string, ...args: any[]): Promise<T> {
    this.invokeCalls.push([methodName, args]);
    if (methodName === 'JoinChannel') {
      return this.joinResult as unknown as T;
    }
    return undefined as unknown as T;
  }

  async start(): Promise<void> {}
  async stop(): Promise<void> {}

  emit(methodName: string, payload: unknown) {
    this.handlers.get(methodName)?.(payload);
  }
}

describe('useChannelConnection', () => {
  it('joins the channel on start and exposes the returned state', async () => {
    const fake = new FakeConnection();
    fake.joinResult = { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false };

    const { result } = renderHook(() => useChannelConnection('kitchen', () => fake));

    await waitFor(() => expect(result.current.state).not.toBeNull());
    expect(result.current.state).toEqual(fake.joinResult);
    expect(result.current.notFound).toBe(false);
  });

  it('flags notFound when the channel does not exist', async () => {
    const fake = new FakeConnection();
    fake.joinResult = null;

    const { result } = renderHook(() => useChannelConnection('missing', () => fake));

    await waitFor(() => expect(result.current.notFound).toBe(true));
  });

  it('updates state when the server pushes ChannelStateChanged', async () => {
    const fake = new FakeConnection();
    fake.joinResult = { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false };

    const { result } = renderHook(() => useChannelConnection('kitchen', () => fake));
    await waitFor(() => expect(result.current.state).not.toBeNull());

    act(() => {
      fake.emit('ChannelStateChanged', { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 20, isPaused: false });
    });

    await waitFor(() => expect(result.current.state?.currentSlideId).toBe(20));
  });

  it('sends the channel id when requesting the next slide', async () => {
    const fake = new FakeConnection();
    fake.joinResult = { channelId: 7, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false };

    const { result } = renderHook(() => useChannelConnection('kitchen', () => fake));
    await waitFor(() => expect(result.current.state).not.toBeNull());

    await act(async () => {
      await result.current.requestNext();
    });

    expect(fake.invokeCalls).toContainEqual(['RequestNextSlide', [7]]);
  });
});
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `cd SynologySlideshow.Web && npm test -- useChannelConnection`
Expected: FAIL — `./useChannelConnection` doesn't exist yet.

- [ ] **Step 5: Implement the hook**

Create `SynologySlideshow.Web/src/hooks/useChannelConnection.ts`:

```ts
import { useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { ChannelState } from '../types';

export interface HubConnectionLike {
  on(methodName: string, callback: (...args: any[]) => void): void;
  invoke<T = void>(methodName: string, ...args: any[]): Promise<T>;
  start(): Promise<void>;
  stop(): Promise<void>;
}

export type ConnectionFactory = () => HubConnectionLike;

const defaultFactory: ConnectionFactory = () =>
  new signalR.HubConnectionBuilder()
    .withUrl('/hub/slideshow')
    .withAutomaticReconnect()
    .build();

export interface ChannelConnectionResult {
  state: ChannelState | null;
  notFound: boolean;
  requestNext(): Promise<void>;
  requestPrevious(): Promise<void>;
  requestJump(slideId: number): Promise<void>;
  requestTogglePause(): Promise<void>;
  requestSwitchAlbum(albumId: number): Promise<void>;
}

export function useChannelConnection(channelName: string, factory: ConnectionFactory = defaultFactory): ChannelConnectionResult {
  const [state, setState] = useState<ChannelState | null>(null);
  const [notFound, setNotFound] = useState(false);
  const connectionRef = useRef<HubConnectionLike | null>(null);

  useEffect(() => {
    let cancelled = false;
    setState(null);
    setNotFound(false);

    const connection = factory();
    connectionRef.current = connection;

    connection.on('ChannelStateChanged', (payload: ChannelState) => {
      if (!cancelled) setState(payload);
    });

    connection
      .start()
      .then(() => connection.invoke<ChannelState | null>('JoinChannel', channelName))
      .then((initial) => {
        if (cancelled) return;
        if (initial === null) {
          setNotFound(true);
        } else {
          setState(initial);
        }
      })
      .catch(() => {
        if (!cancelled) setNotFound(true);
      });

    return () => {
      cancelled = true;
      connection.stop();
    };
  }, [channelName, factory]);

  const withChannelId = (fn: (connection: HubConnectionLike, channelId: number) => Promise<void>) => async () => {
    const connection = connectionRef.current;
    if (!connection || state == null) return;
    await fn(connection, state.channelId);
  };

  return {
    state,
    notFound,
    requestNext: withChannelId((c, id) => c.invoke('RequestNextSlide', id)),
    requestPrevious: withChannelId((c, id) => c.invoke('RequestPreviousSlide', id)),
    requestJump: (slideId: number) => withChannelId((c, id) => c.invoke('RequestJumpToSlide', id, slideId))(),
    requestTogglePause: withChannelId((c, id) => c.invoke('RequestTogglePause', id)),
    requestSwitchAlbum: (albumId: number) => withChannelId((c, id) => c.invoke('RequestSwitchAlbum', id, albumId))()
  };
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd SynologySlideshow.Web && npm test -- useChannelConnection`
Expected: PASS (4 tests).

- [ ] **Step 7: Commit**

```bash
git add SynologySlideshow.Web/package.json SynologySlideshow.Web/package-lock.json SynologySlideshow.Web/vite.config.ts SynologySlideshow.Web/src/test-setup.ts SynologySlideshow.Web/src/types/index.ts SynologySlideshow.Web/src/hooks/useChannelConnection.ts SynologySlideshow.Web/src/hooks/useChannelConnection.test.ts
git commit -m "feat: add channel connection hook and frontend test infrastructure"
```

---

## Task 2: Local redirect-memory for the last-joined channel

**Files:**
- Create: `SynologySlideshow.Web/src/services/channelMemory.ts`
- Create: `SynologySlideshow.Web/src/services/channelMemory.test.ts`

**Interfaces:**
- Produces: `rememberChannel(name: string): void`, `forgetChannel(): void`, `getRememberedChannel(): string | null`. Task 3 calls all three; Task 4 doesn't touch this file.

- [ ] **Step 1: Write the failing tests**

Create `SynologySlideshow.Web/src/services/channelMemory.test.ts`:

```ts
import { beforeEach, describe, expect, it } from 'vitest';
import { rememberChannel, forgetChannel, getRememberedChannel } from './channelMemory';

describe('channelMemory', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('returns null when nothing has been remembered', () => {
    expect(getRememberedChannel()).toBeNull();
  });

  it('returns the remembered channel name after rememberChannel', () => {
    rememberChannel('kitchen');
    expect(getRememberedChannel()).toBe('kitchen');
  });

  it('returns null again after forgetChannel', () => {
    rememberChannel('kitchen');
    forgetChannel();
    expect(getRememberedChannel()).toBeNull();
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd SynologySlideshow.Web && npm test -- channelMemory`
Expected: FAIL — `./channelMemory` doesn't exist yet.

- [ ] **Step 3: Implement it**

Create `SynologySlideshow.Web/src/services/channelMemory.ts`:

```ts
const STORAGE_KEY = 'synology-slideshow-channel-name';

export function rememberChannel(name: string): void {
  try {
    localStorage.setItem(STORAGE_KEY, name);
  } catch {
    // localStorage unavailable (private browsing, blocked storage) — nothing to remember
  }
}

export function forgetChannel(): void {
  try {
    localStorage.removeItem(STORAGE_KEY);
  } catch {
    // localStorage unavailable — nothing to forget
  }
}

export function getRememberedChannel(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd SynologySlideshow.Web && npm test -- channelMemory`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add SynologySlideshow.Web/src/services/channelMemory.ts SynologySlideshow.Web/src/services/channelMemory.test.ts
git commit -m "feat: add local-only channel redirect memory"
```

---

## Task 3: `ChannelView` — the pure-renderer channel page, and its route

**Files:**
- Create: `SynologySlideshow.Web/src/components/ChannelViewPresentation.tsx`
- Create: `SynologySlideshow.Web/src/components/ChannelViewPresentation.test.tsx`
- Create: `SynologySlideshow.Web/src/components/ChannelView.tsx`
- Create: `SynologySlideshow.Web/src/components/ChannelView.test.tsx`
- Create: `SynologySlideshow.Web/src/components/RootRoute.tsx`
- Create: `SynologySlideshow.Web/src/components/RootRoute.test.tsx`
- Modify: `SynologySlideshow.Web/src/components/OverlayMenu.tsx`
- Modify: `SynologySlideshow.Web/src/App.tsx`

**Interfaces:**
- Consumes: `useChannelConnection` (Task 1), `rememberChannel`/`forgetChannel`/`getRememberedChannel` (Task 2), existing `SlideLayer`, `Clock`, `SwipeArea`, `AlbumGrid`, `Settings`, `useSettings`, `useKeyboard`, `getAlbums`/`getAlbumSlides` from `services/api.ts` — all unchanged.
- Produces: route `/:channelName` → `ChannelView`; `OverlayMenu`'s new optional `settingsFooter?: React.ReactNode` prop (rendered under `<Settings>` only on the settings tab) — Task 4 uses this same prop on the anonymous side.

`ChannelView` is a thin container (talks to the hook, handles not-found redirect and remembering); `ChannelViewPresentation` is the pure, prop-driven component that does the actual rendering — split so the rendering logic (including the pause/overlay decoupling from Review Focus) is testable without any real or fake SignalR connection.

- [ ] **Step 1: Write the failing presentation tests**

Create `SynologySlideshow.Web/src/components/ChannelViewPresentation.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { ChannelViewPresentation } from './ChannelViewPresentation';
import * as api from '../services/api';
import { ChannelState } from '../types';

vi.mock('../services/api');

describe('ChannelViewPresentation', () => {
  const baseState: ChannelState = { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false };

  beforeEach(() => {
    vi.mocked(api.getAlbums).mockResolvedValue({ data: [] } as any);
    vi.mocked(api.getAlbumSlides).mockResolvedValue({
      data: [{ id: 10, uri: '/img/10.jpg', description: '', location: '', date: '' }]
    } as any);
  });

  it('fetches and renders the slide matching the pushed currentSlideId', async () => {
    render(
      <ChannelViewPresentation
        state={baseState}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onTogglePause={vi.fn()}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    await waitFor(() => expect(api.getAlbumSlides).toHaveBeenCalledWith(5));
  });

  it('resolves to no rendered slide if the pushed slide id is not in the album', async () => {
    vi.mocked(api.getAlbumSlides).mockResolvedValue({ data: [] } as any);

    const { container } = render(
      <ChannelViewPresentation
        state={baseState}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onTogglePause={vi.fn()}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    await waitFor(() => expect(api.getAlbumSlides).toHaveBeenCalled());
    expect(container.querySelectorAll('.full-screen[style]').length).toBe(0);
  });

  it('opens the overlay and requests a pause toggle on local double-click', () => {
    const onTogglePause = vi.fn();
    const { container } = render(
      <ChannelViewPresentation
        state={baseState}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onTogglePause={onTogglePause}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    fireEvent.doubleClick(container.querySelector('.scrim')!);

    expect(onTogglePause).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('button', { name: 'Albums' })).toBeInTheDocument();
  });

  it('does not show the local overlay when isPaused turns true from a remote push', () => {
    const { rerender } = render(
      <ChannelViewPresentation
        state={baseState}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onTogglePause={vi.fn()}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    rerender(
      <ChannelViewPresentation
        state={{ ...baseState, isPaused: true }}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onTogglePause={vi.fn()}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    expect(screen.queryByRole('button', { name: 'Albums' })).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd SynologySlideshow.Web && npm test -- ChannelViewPresentation`
Expected: FAIL — `./ChannelViewPresentation` doesn't exist yet.

- [ ] **Step 3: Add the `settingsFooter` prop to `OverlayMenu`**

Modify `SynologySlideshow.Web/src/components/OverlayMenu.tsx`:

```tsx
import React, { useState } from 'react';
import { Album, AppSettings } from '../types';
import { Navigation, NavTab } from './Navigation';
import { AlbumGrid } from './AlbumGrid';
import { Settings } from './Settings';

interface OverlayMenuProps {
  albums: Album[];
  currentAlbumId: number;
  settings: AppSettings;
  onSelectAlbum: (album: Album) => void;
  onSettingsChange: (settings: Partial<AppSettings>) => void;
  onClose: () => void;
  settingsFooter?: React.ReactNode;
}

export function OverlayMenu({
  albums,
  currentAlbumId,
  settings,
  onSelectAlbum,
  onSettingsChange,
  onClose,
  settingsFooter
}: OverlayMenuProps) {
  const [activeTab, setActiveTab] = useState<NavTab>('albums');

  return (
    <section
      className="full-screen overlay-menu"
      style={{ zIndex: 100 }}
      onDoubleClick={onClose}
    >
      <Navigation activeTab={activeTab} onTabChange={setActiveTab} />

      {activeTab === 'albums' ? (
        <AlbumGrid
          albums={albums}
          currentAlbumId={currentAlbumId}
          onSelectAlbum={onSelectAlbum}
        />
      ) : (
        <>
          <Settings
            settings={settings}
            onSettingsChange={onSettingsChange}
            onClose={onClose}
          />
          {settingsFooter}
        </>
      )}
    </section>
  );
}
```

- [ ] **Step 4: Implement `ChannelViewPresentation`**

Create `SynologySlideshow.Web/src/components/ChannelViewPresentation.tsx`:

```tsx
import React, { useEffect, useState } from 'react';
import { Album, ChannelState, Slide, SwipeDirection } from '../types';
import { getAlbums, getAlbumSlides } from '../services/api';
import { useSettings } from '../hooks/useSettings';
import { useKeyboard } from '../hooks/useKeyboard';
import { SwipeArea } from './SwipeArea';
import { Clock } from './Clock';
import { SlideLayer } from './SlideLayer';
import { OverlayMenu } from './OverlayMenu';

interface ChannelViewPresentationProps {
  state: ChannelState;
  onNext: () => void;
  onPrevious: () => void;
  onTogglePause: () => void;
  onSwitchAlbum: (albumId: number) => void;
  onLeave: () => void;
}

export function ChannelViewPresentation({
  state,
  onNext,
  onPrevious,
  onTogglePause,
  onSwitchAlbum,
  onLeave
}: ChannelViewPresentationProps) {
  const { settings, updateSettings } = useSettings();
  const [showOverlay, setShowOverlay] = useState(false);
  const [albums, setAlbums] = useState<Album[]>([]);
  const [currentSlide, setCurrentSlide] = useState<Slide | null>(null);

  useEffect(() => {
    getAlbums().then((response) => setAlbums(response.data));
  }, []);

  useEffect(() => {
    document.title = state.name;
  }, [state.name]);

  useEffect(() => {
    if (!state.currentAlbumId || state.currentSlideId == null) {
      setCurrentSlide(null);
      return;
    }
    getAlbumSlides(state.currentAlbumId).then((response) => {
      setCurrentSlide(response.data.find((s) => s.id === state.currentSlideId) ?? null);
    });
  }, [state.currentAlbumId, state.currentSlideId]);

  const toggleOverlayAndPause = () => {
    setShowOverlay((previous) => !previous);
    onTogglePause();
  };

  const handleSwipe = (direction: SwipeDirection) => {
    switch (direction) {
      case SwipeDirection.LeftToRight:
        onPrevious();
        break;
      case SwipeDirection.RightToLeft:
        onNext();
        break;
      case SwipeDirection.TopToBottom:
        setShowOverlay(true);
        if (!state.isPaused) onTogglePause();
        break;
      case SwipeDirection.BottomToTop:
        setShowOverlay(false);
        if (state.isPaused) onTogglePause();
        break;
    }
  };

  useKeyboard({
    onArrowRight: onNext,
    onArrowLeft: onPrevious,
    onSpace: toggleOverlayAndPause
  });

  return (
    <SwipeArea onSwipe={handleSwipe} className="full-screen">
      {currentSlide && (
        <SlideLayer
          key={`current-${currentSlide.id}`}
          slide={currentSlide}
          zoomMode={settings.imageZoomMode}
          showBlurredBackground={settings.showBlurredBackground}
          kenBurnsEffect={settings.kenBurnsEffect}
          fadeIn
          isCurrentSlide
        />
      )}

      <section className="full-screen scrim" onDoubleClick={toggleOverlayAndPause} />
      <Clock />

      {showOverlay && (
        <section className="full-screen overlay-scrim" onDoubleClick={toggleOverlayAndPause} />
      )}

      {showOverlay && (
        <OverlayMenu
          albums={albums}
          currentAlbumId={state.currentAlbumId ?? 0}
          settings={settings}
          onSelectAlbum={(album) => onSwitchAlbum(album.id)}
          onSettingsChange={updateSettings}
          onClose={() => setShowOverlay(false)}
          settingsFooter={
            <div className="channel-leave">
              <p>
                Channel: <strong>{state.name}</strong>
              </p>
              <button onClick={onLeave}>Leave channel</button>
            </div>
          }
        />
      )}
    </SwipeArea>
  );
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd SynologySlideshow.Web && npm test -- ChannelViewPresentation`
Expected: PASS (4 tests).

- [ ] **Step 6: Write the failing `ChannelView` (container) test**

Create `SynologySlideshow.Web/src/components/ChannelView.test.tsx`:

```tsx
import { describe, expect, it, vi } from 'vitest';
import { render, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ChannelView } from './ChannelView';
import * as connectionHook from '../hooks/useChannelConnection';
import * as memory from '../services/channelMemory';

vi.mock('../hooks/useChannelConnection');
vi.mock('../services/channelMemory');

describe('ChannelView', () => {
  it('redirects to / and forgets the channel when the channel is not found', async () => {
    vi.mocked(connectionHook.useChannelConnection).mockReturnValue({
      state: null,
      notFound: true,
      requestNext: vi.fn(),
      requestPrevious: vi.fn(),
      requestJump: vi.fn(),
      requestTogglePause: vi.fn(),
      requestSwitchAlbum: vi.fn()
    });

    render(
      <MemoryRouter initialEntries={['/missing-channel']}>
        <Routes>
          <Route path="/:channelName" element={<ChannelView />} />
          <Route path="/" element={<div>home</div>} />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => expect(memory.forgetChannel).toHaveBeenCalled());
  });

  it('remembers the channel name once state arrives', async () => {
    vi.mocked(connectionHook.useChannelConnection).mockReturnValue({
      state: { channelId: 1, name: 'kitchen', currentAlbumId: null, currentSlideId: null, isPaused: true },
      notFound: false,
      requestNext: vi.fn(),
      requestPrevious: vi.fn(),
      requestJump: vi.fn(),
      requestTogglePause: vi.fn(),
      requestSwitchAlbum: vi.fn()
    });

    render(
      <MemoryRouter initialEntries={['/kitchen']}>
        <Routes>
          <Route path="/:channelName" element={<ChannelView />} />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => expect(memory.rememberChannel).toHaveBeenCalledWith('kitchen'));
  });
});
```

- [ ] **Step 7: Run the test to verify it fails**

Run: `cd SynologySlideshow.Web && npm test -- ChannelView.test`
Expected: FAIL — `./ChannelView` doesn't exist yet.

- [ ] **Step 8: Write the failing `RootRoute` test**

Create `SynologySlideshow.Web/src/components/RootRoute.test.tsx`:

```tsx
import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { RootRoute } from './RootRoute';
import * as memory from '../services/channelMemory';

vi.mock('../services/channelMemory');
vi.mock('./Home', () => ({ Home: () => <div>anonymous home</div> }));

describe('RootRoute', () => {
  it('redirects to the remembered channel when one is stored', () => {
    vi.mocked(memory.getRememberedChannel).mockReturnValue('kitchen');

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="/" element={<RootRoute />} />
          <Route path="/:channelName" element={<div>channel page</div>} />
        </Routes>
      </MemoryRouter>
    );

    expect(screen.getByText('channel page')).toBeInTheDocument();
  });

  it('renders the anonymous Home when nothing is remembered', () => {
    vi.mocked(memory.getRememberedChannel).mockReturnValue(null);

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="/" element={<RootRoute />} />
        </Routes>
      </MemoryRouter>
    );

    expect(screen.getByText('anonymous home')).toBeInTheDocument();
  });
});
```

Run: `cd SynologySlideshow.Web && npm test -- RootRoute`
Expected: FAIL — `./RootRoute` doesn't exist yet.

- [ ] **Step 9: Implement `ChannelView`, `RootRoute`, and wire up the routes**

Create `SynologySlideshow.Web/src/components/ChannelView.tsx`:

```tsx
import React, { useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useChannelConnection } from '../hooks/useChannelConnection';
import { forgetChannel, rememberChannel } from '../services/channelMemory';
import { ChannelViewPresentation } from './ChannelViewPresentation';

export function ChannelView() {
  const { channelName } = useParams<{ channelName: string }>();
  const navigate = useNavigate();
  const connection = useChannelConnection(channelName!);

  useEffect(() => {
    if (connection.notFound) {
      forgetChannel();
      navigate('/', { replace: true });
    }
  }, [connection.notFound, navigate]);

  useEffect(() => {
    if (connection.state) {
      rememberChannel(connection.state.name);
    }
  }, [connection.state]);

  if (!connection.state) {
    return null;
  }

  return (
    <ChannelViewPresentation
      state={connection.state}
      onNext={connection.requestNext}
      onPrevious={connection.requestPrevious}
      onTogglePause={connection.requestTogglePause}
      onSwitchAlbum={connection.requestSwitchAlbum}
      onLeave={() => {
        forgetChannel();
        navigate('/', { replace: true });
      }}
    />
  );
}
```

Create `SynologySlideshow.Web/src/components/RootRoute.tsx`:

```tsx
import React from 'react';
import { Navigate } from 'react-router-dom';
import { Home } from './Home';
import { getRememberedChannel } from '../services/channelMemory';

export function RootRoute() {
  const remembered = getRememberedChannel();
  if (remembered) {
    return <Navigate to={`/${remembered}`} replace />;
  }
  return <Home />;
}
```

Modify `SynologySlideshow.Web/src/App.tsx`:

```tsx
import React from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { RootRoute } from './components/RootRoute';
import { Home } from './components/Home';
import { ChannelView } from './components/ChannelView';
import { UpdateNotification } from './components/UpdateNotification';
import { useVersionCheck } from './hooks/useVersionCheck';

function App() {
  const { updateAvailable, reload, dismiss } = useVersionCheck();

  return (
    <BrowserRouter>
      {updateAvailable && <UpdateNotification onReload={reload} onDismiss={dismiss} />}
      <Routes>
        <Route path="/" element={<RootRoute />} />
        <Route path="/album/:albumId" element={<Home />} />
        <Route path="/:channelName" element={<ChannelView />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  );
}

export default App;
```

- [ ] **Step 10: Run the tests to verify they pass**

Run: `cd SynologySlideshow.Web && npm test -- ChannelView.test`
Expected: PASS (2 tests).

Run: `cd SynologySlideshow.Web && npm test -- RootRoute`
Expected: PASS (2 tests).

- [ ] **Step 11: Run the full frontend test suite and the production build**

Run: `cd SynologySlideshow.Web && npm test && npm run build`
Expected: all tests PASS; build succeeds (this also type-checks every file via `tsc`).

- [ ] **Step 12: Commit**

```bash
git add SynologySlideshow.Web/src/components/ChannelViewPresentation.tsx SynologySlideshow.Web/src/components/ChannelViewPresentation.test.tsx SynologySlideshow.Web/src/components/ChannelView.tsx SynologySlideshow.Web/src/components/ChannelView.test.tsx SynologySlideshow.Web/src/components/RootRoute.tsx SynologySlideshow.Web/src/components/RootRoute.test.tsx SynologySlideshow.Web/src/components/OverlayMenu.tsx SynologySlideshow.Web/src/App.tsx
git commit -m "feat: add channel view route as a pure renderer of server-pushed state"
```

---

## Task 4: "Join a channel" picker in the anonymous settings panel

**Files:**
- Modify: `SynologySlideshow.Web/src/services/api.ts`
- Create: `SynologySlideshow.Web/src/components/JoinChannelPicker.tsx`
- Create: `SynologySlideshow.Web/src/components/JoinChannelPicker.test.tsx`
- Modify: `SynologySlideshow.Web/src/components/Home.tsx`

**Interfaces:**
- Consumes: `OverlayMenu`'s `settingsFooter` prop (Task 3).
- Produces: `getChannels(): Promise<AxiosResponse<ChannelSummary[]>>` in `services/api.ts`.

This is the only change to `Home.tsx`/the anonymous experience in this whole plan — one additive prop passed to the `OverlayMenu` it already renders.

- [ ] **Step 1: Write the failing test**

Create `SynologySlideshow.Web/src/components/JoinChannelPicker.test.tsx`:

```tsx
import { describe, expect, it, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { JoinChannelPicker } from './JoinChannelPicker';
import * as api from '../services/api';

vi.mock('../services/api');

describe('JoinChannelPicker', () => {
  it('lists channels fetched from the server and navigates to the selected one', async () => {
    vi.mocked(api.getChannels).mockResolvedValue({ data: [{ id: 1, name: 'kitchen' }] } as any);

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="/" element={<JoinChannelPicker />} />
          <Route path="/:channelName" element={<div>joined channel page</div>} />
        </Routes>
      </MemoryRouter>
    );

    const button = await screen.findByRole('button', { name: 'kitchen' });
    fireEvent.click(button);

    expect(await screen.findByText('joined channel page')).toBeInTheDocument();
  });

  it('renders nothing when there are no channels yet', async () => {
    vi.mocked(api.getChannels).mockResolvedValue({ data: [] } as any);

    const { container } = render(
      <MemoryRouter>
        <JoinChannelPicker />
      </MemoryRouter>
    );

    await waitFor(() => expect(api.getChannels).toHaveBeenCalled());
    expect(container).toBeEmptyDOMElement();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd SynologySlideshow.Web && npm test -- JoinChannelPicker`
Expected: FAIL — `./JoinChannelPicker` and `api.getChannels` don't exist yet.

- [ ] **Step 3: Add `getChannels` to the API client**

Modify `SynologySlideshow.Web/src/services/api.ts`:

```ts
import axios from 'axios';
import { Album, ChannelSummary, Slide } from '../types';

const api = axios.create({
  baseURL: '/api'
});

export const getAlbums = () => api.get<Album[]>('/albums');

export const getAlbumSlides = (albumId: number) =>
  api.get<Slide[]>(`/albums/${albumId}/slides`);

export const getChannels = () => api.get<ChannelSummary[]>('/channels');
```

- [ ] **Step 4: Implement `JoinChannelPicker`**

Create `SynologySlideshow.Web/src/components/JoinChannelPicker.tsx`:

```tsx
import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { getChannels } from '../services/api';
import { ChannelSummary } from '../types';

export function JoinChannelPicker() {
  const navigate = useNavigate();
  const [channels, setChannels] = useState<ChannelSummary[]>([]);

  useEffect(() => {
    getChannels()
      .then((response) => setChannels(response.data))
      .catch(() => setChannels([]));
  }, []);

  if (channels.length === 0) {
    return null;
  }

  return (
    <div className="channel-picker">
      <h3>Point this device at a channel</h3>
      <ul className="channel-picker-list">
        {channels.map((channel) => (
          <li key={channel.id}>
            <button onClick={() => navigate(`/${channel.name}`)}>{channel.name}</button>
          </li>
        ))}
      </ul>
    </div>
  );
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd SynologySlideshow.Web && npm test -- JoinChannelPicker`
Expected: PASS (2 tests).

- [ ] **Step 6: Wire it into `Home.tsx`**

In `SynologySlideshow.Web/src/components/Home.tsx`, add the import:

```tsx
import { JoinChannelPicker } from './JoinChannelPicker';
```

and pass `settingsFooter={<JoinChannelPicker />}` to the existing `<OverlayMenu>` element (it currently ends with `onClose={() => setIsPaused(false)}`):

```tsx
        <OverlayMenu
          albums={albums}
          currentAlbumId={currentAlbumId}
          settings={settings}
          onSelectAlbum={(album) => selectAlbum(album, false)}
          onSettingsChange={updateSettings}
          onClose={() => setIsPaused(false)}
          settingsFooter={<JoinChannelPicker />}
        />
```

- [ ] **Step 7: Run the full frontend test suite and the production build**

Run: `cd SynologySlideshow.Web && npm test && npm run build`
Expected: all tests PASS; build succeeds.

- [ ] **Step 8: Commit**

```bash
git add SynologySlideshow.Web/src/services/api.ts SynologySlideshow.Web/src/components/JoinChannelPicker.tsx SynologySlideshow.Web/src/components/JoinChannelPicker.test.tsx SynologySlideshow.Web/src/components/Home.tsx
git commit -m "feat: let a device join an existing channel from its settings panel"
```
