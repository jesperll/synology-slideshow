import React from 'react';
import { AppSettings, ImageZoomMode } from '../types';

interface SettingsProps {
  settings: AppSettings;
  onSettingsChange: (settings: Partial<AppSettings>) => void;
  onClose: () => void;
}

const SLIDESHOW_SPEEDS = [15, 30, 60, 120];

export function Settings({ settings, onSettingsChange, onClose }: SettingsProps) {
  const isFitMode = settings.imageZoomMode === ImageZoomMode.Fit;
  const isFillMode = settings.imageZoomMode === ImageZoomMode.Fill;

  // Find the closest speed index for the slider
  const getSpeedIndex = (speed: number) => {
    const index = SLIDESHOW_SPEEDS.indexOf(speed);
    return index >= 0 ? index : 1; // default to 30 seconds
  };

  const handleSpeedChange = (index: number) => {
    onSettingsChange({ slideshowSpeed: SLIDESHOW_SPEEDS[index] });
  };

  return (
    <section className="settings-panel">
      <div className="settings-content">
        <h2>Settings</h2>
        
        <div className="setting-group">
          <h3>Slideshow</h3>
          
          <div className="setting-item">
            <label>
              Slide Duration: <strong>{settings.slideshowSpeed}s</strong>
            </label>
            <div className="slider-container">
              <input
                type="range"
                min="0"
                max="3"
                step="1"
                value={getSpeedIndex(settings.slideshowSpeed)}
                onChange={(e) => handleSpeedChange(parseInt(e.target.value))}
                className="speed-slider"
                list="speed-markers"
              />
              <datalist id="speed-markers">
                <option value="0" label="15s"></option>
                <option value="1" label="30s"></option>
                <option value="2" label="60s"></option>
                <option value="3" label="120s"></option>
              </datalist>
              <div className="slider-labels">
                <span>15s</span>
                <span>30s</span>
                <span>60s</span>
                <span>120s</span>
              </div>
            </div>
          </div>
        </div>

        <div className="setting-group">
          <h3>Image Display</h3>
          <div className="setting-item">
            <label>Zoom Mode:</label>
            <div className="radio-group">
              <label className="radio-label">
                <input
                  type="radio"
                  name="zoomMode"
                  value={ImageZoomMode.Fit}
                  checked={settings.imageZoomMode === ImageZoomMode.Fit}
                  onChange={() => onSettingsChange({ imageZoomMode: ImageZoomMode.Fit })}
                />
                <span>Zoom to Fit</span>
                <small className="radio-description">Show entire image (may have letterboxing)</small>
              </label>
              
              <label className="radio-label">
                <input
                  type="radio"
                  name="zoomMode"
                  value={ImageZoomMode.Fill}
                  checked={settings.imageZoomMode === ImageZoomMode.Fill}
                  onChange={() => onSettingsChange({ imageZoomMode: ImageZoomMode.Fill })}
                />
                <span>Zoom to Fill</span>
                <small className="radio-description">Fill entire screen (may crop image)</small>
              </label>
            </div>
          </div>

          {/* Blurred background option - only shown for Fit mode */}
          {isFitMode && (
            <div className="setting-item">
              <label className="checkbox-label">
                <input
                  type="checkbox"
                  checked={settings.showBlurredBackground}
                  onChange={(e) => onSettingsChange({ showBlurredBackground: e.target.checked })}
                />
                <span>Show blurred background</span>
                <small className="checkbox-description">
                  Display a blurred version of the image behind it (cinematic effect)
                </small>
              </label>
            </div>
          )}

          {/* Ken Burns effect option - only shown for Fill mode */}
          {isFillMode && (
            <div className="setting-item">
              <label className="checkbox-label">
                <input
                  type="checkbox"
                  checked={settings.kenBurnsEffect}
                  onChange={(e) => onSettingsChange({ kenBurnsEffect: e.target.checked })}
                />
                <span>Ken Burns effect</span>
                <small className="checkbox-description">
                  Subtle slow pan and zoom animation (documentary style)
                </small>
              </label>
            </div>
          )}
        </div>

        <button className="close-button" onClick={onClose}>
          Close
        </button>
      </div>
    </section>
  );
}
