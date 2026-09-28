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
