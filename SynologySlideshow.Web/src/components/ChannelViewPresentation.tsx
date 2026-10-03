import React, { useEffect, useState } from 'react';
import { Album, ChannelState, Slide, SwipeDirection } from '../types';
import { getAlbums, getAlbumSlides } from '../services/api';
import { useSettings } from '../hooks/useSettings';
import { useKeyboard } from '../hooks/useKeyboard';
import { useScreenWakeLock } from '../hooks/useScreenWakeLock';
import { useTabVisibility } from '../hooks/useTabVisibility';
import { SwipeArea } from './SwipeArea';
import { Clock } from './Clock';
import { SlideInfo } from './SlideInfo';
import { SlideLayer } from './SlideLayer';
import { OverlayMenu } from './OverlayMenu';
import { JoinChannelPicker } from './JoinChannelPicker';

interface ChannelViewPresentationProps {
  state: ChannelState;
  onNext: () => void;
  onPrevious: () => void;
  onSwitchAlbum: (albumId: number) => void;
  onLeave: () => void;
}

export function ChannelViewPresentation({
  state,
  onNext,
  onPrevious,
  onSwitchAlbum,
  onLeave
}: ChannelViewPresentationProps) {
  const { settings, updateSettings } = useSettings();
  const isTabVisible = useTabVisibility();
  useScreenWakeLock(isTabVisible);
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

  // Showing settings is purely a local overlay - it must never pause the channel, since
  // pause/play is shared across every viewer (and now also driven automatically by viewer
  // presence), so one device opening its own settings would stop the slideshow for everyone
  // else watching too. The slide underneath keeps advancing while the overlay is shown.
  const openOverlay = () => setShowOverlay(true);
  const closeOverlay = () => setShowOverlay(false);

  const handleSwipe = (direction: SwipeDirection) => {
    switch (direction) {
      case SwipeDirection.LeftToRight:
        onPrevious();
        break;
      case SwipeDirection.RightToLeft:
        onNext();
        break;
      case SwipeDirection.TopToBottom:
        openOverlay();
        break;
      case SwipeDirection.BottomToTop:
        closeOverlay();
        break;
    }
  };

  useKeyboard({
    onArrowRight: onNext,
    onArrowLeft: onPrevious,
    onSpace: () => {
      if (showOverlay) closeOverlay();
      else openOverlay();
    }
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

      <section className="full-screen scrim" onDoubleClick={openOverlay} />
      {currentSlide && <SlideInfo slide={currentSlide} />}
      <Clock />

      {showOverlay && (
        <section className="full-screen overlay-scrim" onDoubleClick={closeOverlay} />
      )}

      {showOverlay && (
        <OverlayMenu
          albums={albums}
          currentAlbumId={state.currentAlbumId ?? 0}
          settings={settings}
          onSelectAlbum={(album) => onSwitchAlbum(album.id)}
          onSettingsChange={updateSettings}
          onClose={closeOverlay}
          settingsFooter={
            state.isDefault ? (
              <JoinChannelPicker />
            ) : (
              <div className="channel-leave">
                <p>
                  Channel: <strong>{state.name}</strong>
                </p>
                <button onClick={onLeave}>Leave channel</button>
              </div>
            )
          }
        />
      )}
    </SwipeArea>
  );
}
