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
