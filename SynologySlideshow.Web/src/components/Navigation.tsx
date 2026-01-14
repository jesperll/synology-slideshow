import React, { useState, useEffect } from 'react';

export type NavTab = 'albums' | 'settings';

interface NavigationProps {
  activeTab: NavTab;
  onTabChange: (tab: NavTab) => void;
}

export function Navigation({ activeTab, onTabChange }: NavigationProps) {
  const [isFullscreen, setIsFullscreen] = useState(false);

  useEffect(() => {
    const handleFullscreenChange = () => {
      setIsFullscreen(!!document.fullscreenElement);
    };

    document.addEventListener('fullscreenchange', handleFullscreenChange);
    return () => document.removeEventListener('fullscreenchange', handleFullscreenChange);
  }, []);

  const toggleFullscreen = async () => {
    try {
      if (!document.fullscreenElement) {
        await document.documentElement.requestFullscreen();
      } else {
        await document.exitFullscreen();
      }
    } catch (err) {
      console.error('Error toggling fullscreen:', err);
    }
  };

  return (
    <nav className="nav-bar">
      <div className="nav-tabs">
        <button
          className={`nav-button ${activeTab === 'albums' ? 'active' : ''}`}
          onClick={() => onTabChange('albums')}
        >
          Albums
        </button>
        <button
          className={`nav-button ${activeTab === 'settings' ? 'active' : ''}`}
          onClick={() => onTabChange('settings')}
        >
          Settings
        </button>
      </div>
      <button
        className="fullscreen-button"
        onClick={toggleFullscreen}
        title={isFullscreen ? 'Exit Fullscreen' : 'Enter Fullscreen'}
      >
        {isFullscreen ? '⛶' : '⛶'}
      </button>
    </nav>
  );
}
