import React, { useState } from 'react';
import { Album, AppSettings } from '../types';
import { Navigation, NavTab } from './Navigation';
import { AlbumGrid } from './AlbumGrid';
import { Settings } from './Settings';

interface OverlayMenuProps {
  albums: Album[];
  currentAlbumId: number;
  settings: AppSettings;
  onSelectAlbum: (album: Album) => void;
  onSettingsChange: (settings: Partial<AppSettings>) => void;
  onClose: () => void;
}

export function OverlayMenu({ 
  albums,
  currentAlbumId,
  settings, 
  onSelectAlbum, 
  onSettingsChange, 
  onClose 
}: OverlayMenuProps) {
  const [activeTab, setActiveTab] = useState<NavTab>('albums');

  return (
    <section
      className="full-screen overlay-menu"
      style={{ zIndex: 100 }}
      onDoubleClick={onClose}
    >
      <Navigation activeTab={activeTab} onTabChange={setActiveTab} />
      
      {activeTab === 'albums' ? (
        <AlbumGrid
          albums={albums}
          currentAlbumId={currentAlbumId}
          onSelectAlbum={onSelectAlbum}
        />
      ) : (
        <Settings 
          settings={settings} 
          onSettingsChange={onSettingsChange}
          onClose={onClose}
        />
      )}
    </section>
  );
}
