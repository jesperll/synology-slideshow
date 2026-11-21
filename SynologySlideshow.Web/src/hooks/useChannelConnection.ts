import { useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { ChannelState } from '../types';

export interface HubConnectionLike {
  on(methodName: string, callback: (...args: any[]) => void): void;
  onreconnected(callback: (connectionId?: string) => void): void;
  onclose(callback: (error?: Error) => void): void;
  invoke<T = void>(methodName: string, ...args: any[]): Promise<T>;
  start(): Promise<void>;
  stop(): Promise<void>;
}

export type ConnectionFactory = () => HubConnectionLike;

// Retry forever with a capped backoff (2s, 4s, 6s, ... up to 30s). SignalR's default
// policy gives up after ~4 attempts, which would leave a long-running kiosk display
// permanently disconnected after a longer outage or a redeploy.
const retryDelay = (previousRetryCount: number) => Math.min((previousRetryCount + 1) * 2000, 30000);

const defaultFactory: ConnectionFactory = () =>
  new signalR.HubConnectionBuilder()
    .withUrl('/hub/slideshow')
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (retryContext) => retryDelay(retryContext.previousRetryCount)
    })
    .build();

export interface ChannelConnectionResult {
  state: ChannelState | null;
  notFound: boolean;
  requestNext(): Promise<void>;
  requestPrevious(): Promise<void>;
  requestJump(slideId: number): Promise<void>;
  requestSwitchAlbum(albumId: number): Promise<void>;
}

export function useChannelConnection(channelName: string, factory: ConnectionFactory = defaultFactory): ChannelConnectionResult {
  const [state, setState] = useState<ChannelState | null>(null);
  const [notFound, setNotFound] = useState(false);
  const connectionRef = useRef<HubConnectionLike | null>(null);
  // Re-joins the channel on the current connection; set by the effect so it shares the
  // effect's cancellation flag. Used by request* failure handling.
  const rejoinRef = useRef<() => Promise<void>>(async () => {});
  // Keep the latest factory in a ref rather than the effect's dependency array: callers
  // (tests especially) often pass a fresh factory closure on every render, and depending
  // on it directly would tear down/recreate the connection every render, which combined
  // with the state reset below causes an infinite reconnect loop.
  const factoryRef = useRef(factory);
  factoryRef.current = factory;

  useEffect(() => {
    let cancelled = false;
    let retryTimer: ReturnType<typeof setTimeout> | undefined;
    // Only the most recently started join may apply its result, so a slow join that
    // resolves late (e.g. from before a reconnect) can never overwrite a fresher one.
    let joinSequence = 0;
    setState(null);
    setNotFound(false);

    const connection = factoryRef.current();
    connectionRef.current = connection;

    // Joins (or re-joins) the channel group. Group membership is per-connection, so this
    // must run after every (re)connect. Only a resolved `null` means the channel doesn't
    // exist; a thrown error is a transport problem and propagates to the caller.
    const join = async () => {
      const sequence = ++joinSequence;
      const result = await connection.invoke<ChannelState | null>('JoinChannel', channelName);
      if (cancelled || sequence !== joinSequence) return;
      if (result === null) {
        setNotFound(true);
      } else {
        setState(result);
      }
    };

    const rejoin = () =>
      join().catch((error) => {
        if (!cancelled) console.warn('Failed to re-join channel:', error);
      });
    rejoinRef.current = rejoin;

    connection.on('ChannelStateChanged', (payload: ChannelState) => {
      if (!cancelled) setState(payload);
    });

    connection.onreconnected(() => {
      if (!cancelled) rejoin();
    });

    // Automatic reconnect retries indefinitely, so this only fires if SignalR gives up for
    // some other reason (or when we stop the connection ourselves on unmount). Make one
    // manual restart attempt; if that fails, keep showing the last known state.
    connection.onclose(() => {
      if (cancelled) return;
      connection
        .start()
        .then(() => rejoin())
        .catch((error) => {
          if (!cancelled) console.warn('Failed to restart channel connection:', error);
        });
    });

    // Automatic reconnect only covers connections that were once established, so retry
    // the initial start ourselves (e.g. a kiosk booting while the server is down).
    // A failure here must not be treated as "channel not found".
    const connect = (attempt: number) => {
      connection
        .start()
        .then(
          () => rejoin(),
          (error) => {
            if (cancelled) return;
            console.warn('Failed to connect to channel hub, retrying:', error);
            retryTimer = setTimeout(() => connect(attempt + 1), retryDelay(attempt));
          }
        );
    };
    connect(0);

    return () => {
      cancelled = true;
      if (retryTimer !== undefined) clearTimeout(retryTimer);
      rejoinRef.current = async () => {};
      connection.stop().catch(() => {});
    };
  }, [channelName]);

  const withChannelId = (fn: (connection: HubConnectionLike, channelId: number) => Promise<void>) => async () => {
    const connection = connectionRef.current;
    if (!connection || state == null) return;
    try {
      await fn(connection, state.channelId);
    } catch {
      // The request may have failed because the channel was deleted; re-joining tells us
      // (a `null` result flags notFound) and otherwise refreshes state.
      await rejoinRef.current();
    }
  };

  return {
    state,
    notFound,
    requestNext: withChannelId((c, id) => c.invoke('RequestNextSlide', id)),
    requestPrevious: withChannelId((c, id) => c.invoke('RequestPreviousSlide', id)),
    requestJump: (slideId: number) => withChannelId((c, id) => c.invoke('RequestJumpToSlide', id, slideId))(),
    requestSwitchAlbum: (albumId: number) => withChannelId((c, id) => c.invoke('RequestSwitchAlbum', id, albumId))()
  };
}
