import React from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { Home } from './components/Home';
import { UpdateNotification } from './components/UpdateNotification';
import { useVersionCheck } from './hooks/useVersionCheck';

function App() {
  const { updateAvailable, reload, dismiss } = useVersionCheck();
  
  return (
    <BrowserRouter>
      {updateAvailable && <UpdateNotification onReload={reload} onDismiss={dismiss} />}
      <Routes>
        <Route path="/" element={<Home />} />
        <Route path="/album/:albumId" element={<Home />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  );
}

export default App;
