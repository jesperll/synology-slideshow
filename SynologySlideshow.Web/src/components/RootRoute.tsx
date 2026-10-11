import React from 'react';
import { Navigate } from 'react-router-dom';
import { getRememberedChannel } from '../services/channelMemory';

// Matches the name DefaultChannelSeeder.DefaultChannelName seeds on the backend. The default
// channel can't be renamed, so hardcoding it here (rather than fetching channel metadata just
// to find it) is safe.
export const DEFAULT_CHANNEL_NAME = 'Default';

export function RootRoute() {
  const remembered = getRememberedChannel();
  return <Navigate to={`/${remembered ?? DEFAULT_CHANNEL_NAME}`} replace />;
}
