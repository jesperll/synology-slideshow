import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderHook } from '@testing-library/react';
import { usePresenceConnection } from './usePresenceConnection';

const { start, stop, onclose, withAutomaticReconnect } = vi.hoisted(() => ({
  start: vi.fn(),
  stop: vi.fn(),
  onclose: vi.fn(),
  withAutomaticReconnect: vi.fn()
}));

vi.mock('@microsoft/signalr', () => {
  const connection = { start, stop, onclose };
  const builder = {
    withUrl: vi.fn().mockReturnThis(),
    withAutomaticReconnect: withAutomaticReconnect.mockReturnThis(),
    build: vi.fn().mockReturnValue(connection)
  };
  return {
    HubConnectionBuilder: vi.fn(() => builder)
  };
});

describe('usePresenceConnection', () => {
  beforeEach(() => {
    start.mockReset().mockResolvedValue(undefined);
    stop.mockReset().mockResolvedValue(undefined);
    onclose.mockReset();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('starts a hub connection on mount and stops it on unmount', () => {
    const { unmount } = renderHook(() => usePresenceConnection());

    expect(start).toHaveBeenCalledTimes(1);

    unmount();

    expect(stop).toHaveBeenCalledTimes(1);
  });

  it('uses a reconnect policy that keeps retrying with a capped backoff', () => {
    renderHook(() => usePresenceConnection());

    const policy = withAutomaticReconnect.mock.calls[withAutomaticReconnect.mock.calls.length - 1][0];
    expect(policy.nextRetryDelayInMilliseconds({ previousRetryCount: 0 })).toBe(2000);
    expect(policy.nextRetryDelayInMilliseconds({ previousRetryCount: 100 })).toBe(30000);
  });

  it('retries a failed initial start until it succeeds', async () => {
    vi.useFakeTimers();
    start.mockRejectedValueOnce(new Error('server down')).mockResolvedValue(undefined);

    const { unmount } = renderHook(() => usePresenceConnection());
    expect(start).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(2000);

    expect(start).toHaveBeenCalledTimes(2);
    unmount();
  });

  it('stops retrying once unmounted', async () => {
    vi.useFakeTimers();
    start.mockRejectedValue(new Error('server down'));

    const { unmount } = renderHook(() => usePresenceConnection());
    await vi.advanceTimersByTimeAsync(0);
    unmount();

    await vi.advanceTimersByTimeAsync(60000);

    expect(start).toHaveBeenCalledTimes(1);
  });
});
