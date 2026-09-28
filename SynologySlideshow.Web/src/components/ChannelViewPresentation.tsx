import React, { useEffect, useState } from 'react';
import { Album, ChannelState, Slide, SwipeDirection } from '../types';
import { getAlbums, getAlbumSlides } from '../services/api';
import { useSettings } from '../hooks/useSettings';
import { useKeyboard } from '../hooks/useKeyboard';
import { SwipeArea } from './SwipeArea';
import { Clock } from './Clock';
import { SlideLayer } from './SlideLayer';
import { OverlayMenu } from './OverlayMenu';

interface ChannelViewPresentationProps {
  state: ChannelState;
  onNext: () => void;
  onPrevious: () => void;
  onTogglePause: () => void;
  onSwitchAlbum: (albumId: number) => void;
  onLeave: () => void;
}

export function ChannelViewPresentation({
  state,
  onNext,
  onPrevious,
  onTogglePause,
  onSwitchAlbum,
  onLeave
}: ChannelViewPresentationProps) {
  const { settings, updateSettings } = useSettings();
  const [showOverlay, setShowOverlay] = useState(false);
  const [albums, setAlbums] = useState<Album[]>([]);
  const [currentSlide, setCurrentSlide] = useState<Slide | null>(null);

  useEffect(() => {
    getAlbums()
      .then((response) => setAlbums(response.data))
      .catch((error) => console.error('Failed to load albums:', error));
  }, []);

  useEffect(() => {
    document.title = state.name;
  }, [state.name]);

  useEffect(() => {
    if (!state.currentAlbumId || state.currentSlideId == null) {
      setCurrentSlide(null);
      return;
    }
    getAlbumSlides(state.currentAlbumId)
      .then((response) => {
        setCurrentSlide(response.data.find((s) => s.id === state.currentSlideId) ?? null);
      })
      .catch((error) => console.error('Failed to load album slides:', error));
  }, [state.currentAlbumId, state.currentSlideId]);

  const toggleOverlayAndPause = () => {
    setShowOverlay((previous) => !previous);
    onTogglePause();
  };

  const handleSwipe = (direction: SwipeDirection) => {
    switch (direction) {
      case SwipeDirection.LeftToRight:
        onPrevious();
        break;
      case SwipeDirection.RightToLeft:
        onNext();
        break;
      case SwipeDirection.TopToBottom:
        setShowOverlay(true);
        if (!state.isPaused) onTogglePause();
        break;
      case SwipeDirection.BottomToTop:
        setShowOverlay(false);
        if (state.isPaused) onTogglePause();
        break;
    }
  };

  useKeyboard({
    onArrowRight: onNext,
    onArrowLeft: onPrevious,
    onSpace: toggleOverlayAndPause
  });

  return (
    <SwipeArea onSwipe={handleSwipe} className="full-screen">
      {currentSlide && (
        <SlideLayer
          key={`current-${currentSlide.id}`}
          slide={currentSlide}
          zoomMode={settings.imageZoomMode}
          showBlurredBackground={settings.showBlurredBackground}
          kenBurnsEffect={settings.kenBurnsEffect}
          fadeIn
          isCurrentSlide
        />
      )}

      <section className="full-screen scrim" onDoubleClick={toggleOverlayAndPause} />
      <Clock />

      {showOverlay && (
        <section className="full-screen overlay-scrim" onDoubleClick={toggleOverlayAndPause} />
      )}

      {showOverlay && (
        <OverlayMenu
          albums={albums}
          currentAlbumId={state.currentAlbumId ?? 0}
          settings={settings}
          onSelectAlbum={(album) => onSwitchAlbum(album.id)}
          onSettingsChange={updateSettings}
          onClose={() => setShowOverlay(false)}
          settingsFooter={
            <div className="channel-leave">
              <p>
                Channel: <strong>{state.name}</strong>
              </p>
              <button onClick={onLeave}>Leave channel</button>
            </div>
          }
        />
      )}
    </SwipeArea>
  );
}
