import { useState, useEffect } from 'react';
import { AppSettings, ImageZoomMode } from '../types';

const SETTINGS_STORAGE_KEY = 'synology-slideshow-settings';

const DEFAULT_SETTINGS: AppSettings = {
  imageZoomMode: ImageZoomMode.Fit,
  showBlurredBackground: true,
  kenBurnsEffect: true,
  slideshowSpeed: 30
};

export function useSettings() {
  const [settings, setSettings] = useState<AppSettings>(DEFAULT_SETTINGS);

  // Load settings from localStorage on mount
  useEffect(() => {
    try {
      const savedSettings = localStorage.getItem(SETTINGS_STORAGE_KEY);
      if (savedSettings) {
        const parsed = JSON.parse(savedSettings);
        setSettings({ ...DEFAULT_SETTINGS, ...parsed });
      }
    } catch (error) {
      console.error('Failed to load settings:', error);
    }
  }, []);

  // Save settings to localStorage whenever they change
  const updateSettings = (newSettings: Partial<AppSettings>) => {
    setSettings(prev => {
      const updated = { ...prev, ...newSettings };
      try {
        localStorage.setItem(SETTINGS_STORAGE_KEY, JSON.stringify(updated));
      } catch (error) {
        console.error('Failed to save settings:', error);
      }
      return updated;
    });
  };

  return { settings, updateSettings };
}
