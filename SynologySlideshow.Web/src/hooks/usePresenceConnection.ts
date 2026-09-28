import { useEffect } from 'react';
import * as signalR from '@microsoft/signalr';

export function usePresenceConnection(): void {
  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hub/slideshow')
      .withAutomaticReconnect()
      .build();

    connection.start().catch(() => {
      // best-effort presence registration; the anonymous slideshow works regardless of connectivity here
    });

    return () => {
      connection.stop();
    };
  }, []);
}
