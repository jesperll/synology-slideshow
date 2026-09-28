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
  // Keep the latest factory in a ref rather than the effect's dependency array: callers
  // (tests especially) often pass a fresh factory closure on every render, and depending
  // on it directly would tear down/recreate the connection every render, which combined
  // with the state reset below causes an infinite reconnect loop.
  const factoryRef = useRef(factory);
  factoryRef.current = factory;

  useEffect(() => {
    let cancelled = false;
    setState(null);
    setNotFound(false);

    const connection = factoryRef.current();
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
  }, [channelName]);

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
