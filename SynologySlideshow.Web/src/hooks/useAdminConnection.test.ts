import { describe, expect, it } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { useAdminConnection } from './useAdminConnection';
import { HubConnectionLike } from './useChannelConnection';
import { AdminSnapshot, ChannelState } from '../types';

class FakeConnection implements HubConnectionLike {
  handlers = new Map<string, (...args: any[]) => void>();
  invokeCalls: [string, any[]][] = [];
  snapshot: AdminSnapshot = { anonymousCount: 0, channels: [] };
  linkResult: { success: boolean; error: string | null } = { success: true, error: null };

  on(methodName: string, callback: (...args: any[]) => void): void {
    this.handlers.set(methodName, callback);
  }

  reconnectedHandler: ((connectionId?: string) => void) | null = null;

  onreconnected(callback: (connectionId?: string) => void): void {
    this.reconnectedHandler = callback;
  }
  onclose(): void {}

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

  emitReconnected() {
    this.reconnectedHandler?.('new-connection-id');
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
      channels: [{ channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, viewerCount: 1, linkedChannelIds: [] }]
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
  it('re-joins the admin group and refreshes the snapshot after a reconnect', async () => {
    const fake = new FakeConnection();
    fake.snapshot = { anonymousCount: 1, channels: [] };
    const factory = () => fake;

    const { result } = renderHook(() => useAdminConnection(factory));
    await waitFor(() => expect(result.current.snapshot?.anonymousCount).toBe(1));
    const joinsBefore = fake.invokeCalls.filter(([name]) => name === 'JoinAdmin').length;

    fake.snapshot = { anonymousCount: 4, channels: [] };
    act(() => {
      fake.emitReconnected();
    });

    await waitFor(() => expect(result.current.snapshot?.anonymousCount).toBe(4));
    expect(fake.invokeCalls.filter(([name]) => name === 'JoinAdmin').length).toBe(joinsBefore + 1);
  });

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
});
