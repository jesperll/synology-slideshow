import React from 'react';

export type NavTab = 'albums' | 'settings';

interface NavigationProps {
  activeTab: NavTab;
  onTabChange: (tab: NavTab) => void;
}

export function Navigation({ activeTab, onTabChange }: NavigationProps) {
  return (
    <nav className="nav-bar">
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
    </nav>
  );
}
