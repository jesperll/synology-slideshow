import { useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { AdminSnapshot, ChannelState } from '../types';
import { ConnectionFactory, HubConnectionLike } from './useChannelConnection';
import { hubReconnectPolicy, hubRetryDelay } from './hubRetryPolicy';

const defaultFactory: ConnectionFactory = () =>
  new signalR.HubConnectionBuilder()
    .withUrl('/hub/slideshow')
    .withAutomaticReconnect(hubReconnectPolicy)
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
    let retryTimer: ReturnType<typeof setTimeout> | undefined;
    const connection = factory();
    connectionRef.current = connection;

    // Joins (or re-joins) the admin group. Group membership and the server's admin
    // tracking are per-connection, so this must run after every (re)connect - otherwise
    // the page stops receiving pushes and is counted as an anonymous viewer.
    const joinAdmin = () =>
      connection
        .invoke<AdminSnapshot>('JoinAdmin')
        .then((joined) => {
          if (!cancelled) setSnapshot(joined);
        })
        .catch((error) => {
          if (!cancelled) console.warn('Failed to join admin group:', error);
        });

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

    connection.onreconnected(() => {
      if (!cancelled) joinAdmin();
    });

    // Automatic reconnect only covers connections that were once established, so retry
    // the initial start ourselves (and restart if SignalR ever gives up on a connection).
    const connect = (attempt: number) => {
      connection.start().then(
        () => joinAdmin(),
        (error) => {
          if (cancelled) return;
          console.warn('Failed to connect to admin hub, retrying:', error);
          retryTimer = setTimeout(() => connect(attempt + 1), hubRetryDelay(attempt));
        }
      );
    };

    connection.onclose(() => {
      if (!cancelled) connect(0);
    });

    connect(0);

    return () => {
      cancelled = true;
      if (retryTimer !== undefined) clearTimeout(retryTimer);
      connection.stop().catch(() => {});
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
