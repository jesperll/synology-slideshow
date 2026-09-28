import React from 'react';
import { Navigate } from 'react-router-dom';
import { Home } from './Home';
import { getRememberedChannel } from '../services/channelMemory';

export function RootRoute() {
  const remembered = getRememberedChannel();
  if (remembered) {
    return <Navigate to={`/${remembered}`} replace />;
  }
  return <Home />;
}
