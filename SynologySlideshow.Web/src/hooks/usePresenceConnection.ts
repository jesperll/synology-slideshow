import { useEffect } from 'react';
import * as signalR from '@microsoft/signalr';
import { hubReconnectPolicy, hubRetryDelay } from './hubRetryPolicy';

export function usePresenceConnection(): void {
  useEffect(() => {
    let cancelled = false;
    let retryTimer: ReturnType<typeof setTimeout> | undefined;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hub/slideshow')
      .withAutomaticReconnect(hubReconnectPolicy)
      .build();

    // Best-effort presence registration; the anonymous slideshow works regardless of
    // connectivity here. Automatic reconnect only covers connections that were once
    // established, so retry the initial start ourselves (e.g. the server is mid-redeploy).
    const connect = (attempt: number) => {
      connection.start().catch(() => {
        if (cancelled) return;
        retryTimer = setTimeout(() => connect(attempt + 1), hubRetryDelay(attempt));
      });
    };

    // Automatic reconnect retries indefinitely, so this only fires if SignalR gives up for
    // some other reason (or when we stop the connection ourselves on unmount).
    connection.onclose(() => {
      if (!cancelled) connect(0);
    });

    connect(0);

    return () => {
      cancelled = true;
      if (retryTimer !== undefined) clearTimeout(retryTimer);
      connection.stop().catch(() => {});
    };
  }, []);
}
