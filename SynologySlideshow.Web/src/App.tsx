import React from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { RootRoute } from './components/RootRoute';
import { Home } from './components/Home';
import { ChannelView } from './components/ChannelView';
import { AdminPage } from './components/AdminPage';
import { UpdateNotification } from './components/UpdateNotification';
import { useVersionCheck } from './hooks/useVersionCheck';

function App() {
  const { updateAvailable, reload, dismiss } = useVersionCheck();

  return (
    <BrowserRouter>
      {updateAvailable && <UpdateNotification onReload={reload} onDismiss={dismiss} />}
      <Routes>
        <Route path="/" element={<RootRoute />} />
        <Route path="/album/:albumId" element={<Home />} />
        <Route path="/admin" element={<AdminPage />} />
        <Route path="/:channelName" element={<ChannelView />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  );
}

export default App;
