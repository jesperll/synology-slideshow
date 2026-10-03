import { describe, expect, it, vi } from 'vitest';
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
    if (this.failingMethods.has(methodName)) {
      throw new Error(`${methodName} failed`);
    }
    if (methodName === 'JoinChannel') {
      return this.joinResult as unknown as T;
    }
    return undefined as unknown as T;
  }

  reconnectedCallback: ((connectionId?: string) => void) | null = null;
  closeCallback: ((error?: Error) => void) | null = null;
  startError: Error | null = null;
  startCalls = 0;
  failingMethods = new Set<string>();

  onreconnected(callback: (connectionId?: string) => void): void {
    this.reconnectedCallback = callback;
  }

  onclose(callback: (error?: Error) => void): void {
    this.closeCallback = callback;
  }

  async start(): Promise<void> {
    this.startCalls++;
    if (this.startError) throw this.startError;
  }
  async stop(): Promise<void> {}

  emit(methodName: string, payload: unknown) {
    this.handlers.get(methodName)?.(payload);
  }

  triggerReconnected() {
    this.reconnectedCallback?.('new-connection-id');
  }

  triggerClose(error?: Error) {
    this.closeCallback?.(error);
  }

  joinCount() {
    return this.invokeCalls.filter(([name]) => name === 'JoinChannel').length;
  }
}

describe('useChannelConnection', () => {
  it('joins the channel on start and exposes the returned state', async () => {
    const fake = new FakeConnection();
    fake.joinResult = { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, isDefault: false };

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
    fake.joinResult = { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, isDefault: false };

    const { result } = renderHook(() => useChannelConnection('kitchen', () => fake));
    await waitFor(() => expect(result.current.state).not.toBeNull());

    act(() => {
      fake.emit('ChannelStateChanged', { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 20, isPaused: false, isDefault: false });
    });

    await waitFor(() => expect(result.current.state?.currentSlideId).toBe(20));
  });

  it('sends the channel id when requesting the next slide', async () => {
    const fake = new FakeConnection();
    fake.joinResult = { channelId: 7, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, isDefault: false };

    const { result } = renderHook(() => useChannelConnection('kitchen', () => fake));
    await waitFor(() => expect(result.current.state).not.toBeNull());

    await act(async () => {
      await result.current.requestNext();
    });

    expect(fake.invokeCalls).toContainEqual(['RequestNextSlide', [7]]);
  });

  it('re-joins the channel after SignalR reconnects and applies the new state', async () => {
    const fake = new FakeConnection();
    fake.joinResult = { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, isDefault: false };

    const { result } = renderHook(() => useChannelConnection('kitchen', () => fake));
    await waitFor(() => expect(result.current.state).not.toBeNull());
    expect(fake.joinCount()).toBe(1);

    fake.joinResult = { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 42, isPaused: true, isDefault: false };
    act(() => {
      fake.triggerReconnected();
    });

    await waitFor(() => expect(result.current.state?.currentSlideId).toBe(42));
    expect(fake.joinCount()).toBe(2);
    expect(fake.invokeCalls[fake.invokeCalls.length - 1]).toEqual(['JoinChannel', ['kitchen']]);
    expect(result.current.state?.isPaused).toBe(true);
  });

  it('flags notFound if the channel is gone when re-joining after a reconnect', async () => {
    const fake = new FakeConnection();
    fake.joinResult = { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, isDefault: false };

    const { result } = renderHook(() => useChannelConnection('kitchen', () => fake));
    await waitFor(() => expect(result.current.state).not.toBeNull());

    fake.joinResult = null;
    act(() => {
      fake.triggerReconnected();
    });

    await waitFor(() => expect(result.current.notFound).toBe(true));
  });

  it('restarts and re-joins once when the connection closes', async () => {
    const fake = new FakeConnection();
    fake.joinResult = { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, isDefault: false };

    const { result } = renderHook(() => useChannelConnection('kitchen', () => fake));
    await waitFor(() => expect(result.current.state).not.toBeNull());

    fake.joinResult = { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 11, isPaused: false, isDefault: false };
    act(() => {
      fake.triggerClose(new Error('connection lost'));
    });

    await waitFor(() => expect(result.current.state?.currentSlideId).toBe(11));
    expect(fake.startCalls).toBe(2);
  });

  it('keeps the last known state if restarting after close fails', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const fake = new FakeConnection();
    fake.joinResult = { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, isDefault: false };

    const { result } = renderHook(() => useChannelConnection('kitchen', () => fake));
    await waitFor(() => expect(result.current.state).not.toBeNull());

    fake.startError = new Error('server down');
    act(() => {
      fake.triggerClose(new Error('connection lost'));
    });

    await waitFor(() => expect(warn).toHaveBeenCalled());
    expect(fake.startCalls).toBe(2);
    expect(fake.joinCount()).toBe(1);
    expect(result.current.state?.currentSlideId).toBe(10);
    expect(result.current.notFound).toBe(false);
    warn.mockRestore();
  });

  it('does not flag notFound when the initial connection fails', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const fake = new FakeConnection();
    fake.startError = new Error('network down');

    const { result, unmount } = renderHook(() => useChannelConnection('kitchen', () => fake));

    await waitFor(() => expect(warn).toHaveBeenCalled());
    expect(result.current.notFound).toBe(false);
    expect(result.current.state).toBeNull();
    unmount();
    warn.mockRestore();
  });

  it('retries the initial connection after a failed start', async () => {
    vi.useFakeTimers();
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    try {
      const fake = new FakeConnection();
      fake.startError = new Error('network down');
      fake.joinResult = { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, isDefault: false };

      const { result } = renderHook(() => useChannelConnection('kitchen', () => fake));
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });
      expect(fake.startCalls).toBe(1);

      fake.startError = null;
      await act(async () => {
        await vi.advanceTimersByTimeAsync(2000);
      });

      expect(fake.startCalls).toBe(2);
      expect(result.current.state).toEqual(fake.joinResult);
    } finally {
      warn.mockRestore();
      vi.useRealTimers();
    }
  });

  it('flags notFound when a request fails and the channel no longer exists', async () => {
    const fake = new FakeConnection();
    fake.joinResult = { channelId: 7, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, isDefault: false };

    const { result } = renderHook(() => useChannelConnection('kitchen', () => fake));
    await waitFor(() => expect(result.current.state).not.toBeNull());

    fake.failingMethods.add('RequestNextSlide');
    fake.joinResult = null;
    await act(async () => {
      await result.current.requestNext();
    });

    expect(fake.joinCount()).toBe(2);
    expect(result.current.notFound).toBe(true);
  });

  it('refreshes state and swallows the error when a request fails but the channel still exists', async () => {
    const fake = new FakeConnection();
    fake.joinResult = { channelId: 7, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, isDefault: false };

    const { result } = renderHook(() => useChannelConnection('kitchen', () => fake));
    await waitFor(() => expect(result.current.state).not.toBeNull());

    fake.failingMethods.add('RequestTogglePause');
    fake.joinResult = { channelId: 7, name: 'kitchen', currentAlbumId: 5, currentSlideId: 12, isPaused: true, isDefault: false };
    await act(async () => {
      await expect(result.current.requestTogglePause()).resolves.toBeUndefined();
    });

    expect(result.current.state?.currentSlideId).toBe(12);
    expect(result.current.notFound).toBe(false);
  });
});
