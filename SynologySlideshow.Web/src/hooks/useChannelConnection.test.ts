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
