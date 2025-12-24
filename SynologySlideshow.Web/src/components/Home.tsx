import React, { useState, useEffect, useCallback, useRef } from 'react';
import { useParams, useNavigate, useLocation } from 'react-router-dom';
import { Album, Slide, SwipeDirection } from '../types';
import { getAlbums, getAlbumSlides } from '../services/api';
import { useKeyboard } from '../hooks/useKeyboard';
import { useScreenWakeLock } from '../hooks/useScreenWakeLock';
import { useTabVisibility } from '../hooks/useTabVisibility';
import { useSettings } from '../hooks/useSettings';
import { SwipeArea } from './SwipeArea';
import { Clock } from './Clock';
import { SlideInfo } from './SlideInfo';
import { SlideLayer } from './SlideLayer';
import { OverlayMenu } from './OverlayMenu';

const STORAGE_KEY = 'synology-slideshow-selected-album-id';

function shuffle<T>(array: T[]): T[] {
  const result = [...array];
  for (let i = result.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [result[i], result[j]] = [result[j], result[i]];
  }
  return result;
}

export function Home() {
  const { albumId } = useParams<{ albumId?: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const { settings, updateSettings } = useSettings();
  
  const [albums, setAlbums] = useState<Album[]>([]);
  const [currentAlbumId, setCurrentAlbumId] = useState<number>(0);
  const [currentAlbumName, setCurrentAlbumName] = useState<string>('');
  const [slides, setSlides] = useState<Slide[]>([]);
  const [currentSlideIndex, setCurrentSlideIndex] = useState<number>(-1);
  const [currentSlide, setCurrentSlide] = useState<Slide | null>(null);
  const [previousSlide, setPreviousSlide] = useState<Slide | null>(null);
  const [isPaused, setIsPaused] = useState<boolean>(false);
  
  const slideTimerRef = useRef<number | null>(null);

  // Get slide speed in milliseconds from settings
  const slideSpeed = settings.slideshowSpeed * 1000;

  // Track tab visibility - slideshow should only advance when tab is active
  const isTabVisible = useTabVisibility();

  // Initialize screen wake lock (only when tab is visible)
  useScreenWakeLock(isTabVisible);

  // Update document title when album name changes
  useEffect(() => {
    document.title = currentAlbumName || 'Synology Slideshow';
  }, [currentAlbumName]);

  // Load albums on mount and restore saved album selection
  useEffect(() => {
    const loadAlbums = async () => {
      try {
        const response = await getAlbums();
        const albumsData = response.data;
        setAlbums(albumsData);
        
        let albumToSelect: Album | undefined;
        const isRootPath = location.pathname === '/';

        // Priority 1: Album from URL parameter
        if (albumId) {
          albumToSelect = albumsData.find(a => a.id === parseInt(albumId, 10));
        }

        // Priority 2: Album from localStorage (only if on root path)
        if (!albumToSelect && isRootPath) {
          const savedAlbumId = localStorage.getItem(STORAGE_KEY);
          if (savedAlbumId) {
            albumToSelect = albumsData.find(a => a.id === parseInt(savedAlbumId, 10));
          }
        }

        // Priority 3: First album as fallback
        if (!albumToSelect && albumsData.length > 0) {
          albumToSelect = albumsData[0];
        }

        if (albumToSelect) {
          // If we're at root path, redirect to the album URL
          if (isRootPath) {
            navigate(`/album/${albumToSelect.id}`, { replace: true });
          }
          await selectAlbum(albumToSelect, !isRootPath);
        }
      } catch (error) {
        console.error('Failed to load albums:', error);
      }
    };

    loadAlbums();
  }, [albumId, location.pathname]);

  // Manage slide timer based on pause state, speed setting, and tab visibility
  useEffect(() => {
    if (slideTimerRef.current) {
      clearInterval(slideTimerRef.current);
      slideTimerRef.current = null;
    }

    // Only advance slideshow when tab is visible, not paused, and slides exist
    if (isTabVisible && !isPaused && slides.length > 0) {
      slideTimerRef.current = window.setInterval(() => {
        nextSlide(1);
      }, slideSpeed);
    }

    return () => {
      if (slideTimerRef.current) {
        clearInterval(slideTimerRef.current);
        slideTimerRef.current = null;
      }
    };
  }, [isTabVisible, isPaused, slides, slideSpeed]);

  const selectAlbum = async (album: Album, skipNavigation: boolean = false) => {
    try {
      const response = await getAlbumSlides(album.id);
      const slidesData = shuffle(response.data);
      
      setCurrentAlbumId(album.id);
      setCurrentAlbumName(album.name);
      setSlides(slidesData);
      setCurrentSlideIndex(-1);
      setCurrentSlide(null);
      setPreviousSlide(null);
      
      // Save selected album to localStorage
      localStorage.setItem(STORAGE_KEY, album.id.toString());
      
      // Update URL without triggering a page reload (unless already handled)
      if (!skipNavigation) {
        navigate(`/album/${album.id}`, { replace: true });
      }
      
      // Advance to first slide
      if (slidesData.length > 0) {
        setPreviousSlide(null);
        setCurrentSlide(slidesData[0]);
        setCurrentSlideIndex(0);
      }
      
      setIsPaused(false);
    } catch (error) {
      console.error('Failed to load album slides:', error);
    }
  };

  const nextSlide = useCallback((offset: number) => {
    if (slides.length === 0) return;

    setCurrentSlideIndex((prevIndex) => {
      const newIndex = (prevIndex + offset + slides.length) % slides.length;
      setPreviousSlide(currentSlide);
      setCurrentSlide(slides[newIndex]);
      return newIndex;
    });

    // Reset the timer when manually navigating
    if (slideTimerRef.current) {
      clearInterval(slideTimerRef.current);
      slideTimerRef.current = null;
    }

    // Restart the timer if not paused and tab is visible
    if (!isPaused && slides.length > 0 && isTabVisible) {
      slideTimerRef.current = window.setInterval(() => {
        nextSlide(1);
      }, slideSpeed);
    }

    setIsPaused(false);
  }, [slides, currentSlide, isPaused, slideSpeed, isTabVisible]);

  const handleSwipe = (direction: SwipeDirection) => {
    switch (direction) {
      case SwipeDirection.LeftToRight:
        nextSlide(1);
        break;
      case SwipeDirection.RightToLeft:
        nextSlide(-1);
        break;
      case SwipeDirection.TopToBottom:
        setIsPaused(true);
        break;
      case SwipeDirection.BottomToTop:
        setIsPaused(false);
        break;
    }
  };

  const togglePause = () => setIsPaused(!isPaused);

  // Keyboard shortcuts
  useKeyboard({
    onArrowRight: () => nextSlide(1),
    onArrowLeft: () => nextSlide(-1),
    onSpace: togglePause
  });

  return (
    <SwipeArea onSwipe={handleSwipe} className="full-screen">
      {/* Previous slide layer (with fade-out effect, no blurred background) */}
      {previousSlide && (
        <SlideLayer
          key={`prev-${previousSlide.id}`}
          slide={previousSlide}
          zoomMode={settings.imageZoomMode}
          showBlurredBackground={settings.showBlurredBackground}
          kenBurnsEffect={settings.kenBurnsEffect}
          fadeOut={true}
          isCurrentSlide={false}
        />
      )}

      {/* Current slide layer (with fade-in animation and blurred background) */}
      {currentSlide && (
        <SlideLayer
          key={`current-${currentSlide.id}`}
          slide={currentSlide}
          zoomMode={settings.imageZoomMode}
          showBlurredBackground={settings.showBlurredBackground}
          kenBurnsEffect={settings.kenBurnsEffect}
          fadeIn={true}
          isCurrentSlide={true}
        />
      )}
      
      {/* Bottom gradient scrim (behind text) */}
      <section className="full-screen scrim" onDoubleClick={togglePause} />

      {/* Metadata text and clock (on top of bottom scrim) */}
      {currentSlide && <SlideInfo slide={currentSlide} />}
      <Clock />

      {/* Overlay scrim (when paused - darkens everything) */}
      {isPaused && (
        <section 
          className="full-screen overlay-scrim" 
          onDoubleClick={togglePause}
        />
      )}

      {/* Overlay menu with navigation and content (on top of everything) */}
      {isPaused && (
        <OverlayMenu
          albums={albums}
          currentAlbumId={currentAlbumId}
          settings={settings}
          onSelectAlbum={(album) => selectAlbum(album, false)}
          onSettingsChange={updateSettings}
          onClose={() => setIsPaused(false)}
        />
      )}
    </SwipeArea>
  );
}
