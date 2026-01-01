import React, { useMemo } from 'react';
import { Slide, ImageZoomMode } from '../types';

interface SlideLayerProps {
  slide: Slide | null;
  zoomMode: ImageZoomMode;
  showBlurredBackground: boolean;
  kenBurnsEffect: boolean;
  fadeIn?: boolean;
  fadeOut?: boolean;
  isCurrentSlide?: boolean;
  className?: string;
}

const KEN_BURNS_ANIMATIONS = [
  'ken-burns-zoom-in',
  'ken-burns-zoom-out',
  'ken-burns-pan-left',
  'ken-burns-pan-right',
  'ken-burns-pan-up',
  'ken-burns-pan-down',
  'ken-burns-diagonal-1',
  'ken-burns-diagonal-2'
];

export function SlideLayer({ 
  slide, 
  zoomMode, 
  showBlurredBackground,
  kenBurnsEffect,
  fadeIn = false,
  fadeOut = false,
  isCurrentSlide = false,
  className = ''
}: SlideLayerProps) {
  // Pick a random Ken Burns animation for this slide (stable across re-renders)
  const kenBurnsClass = useMemo(() => {
    if (!slide || !kenBurnsEffect || zoomMode !== ImageZoomMode.Fill) {
      return '';
    }
    const index = slide.id % KEN_BURNS_ANIMATIONS.length;
    return KEN_BURNS_ANIMATIONS[index];
  }, [slide?.id, kenBurnsEffect, zoomMode]);

  if (!slide) {
    return null;
  }

  // Only show blur for the current slide in Fit mode
  const shouldShowBlur = isCurrentSlide && zoomMode === ImageZoomMode.Fit && showBlurredBackground;
  const animationClass = fadeIn ? 'fadeIn' : fadeOut ? 'fadeOut' : '';

  return (
    <>
      {/* Blurred background layer (only for current slide in Fit mode) */}
      {shouldShowBlur && (
        <section
          className={`full-screen blurred-background ${animationClass} ${className}`}
          style={{ 
            backgroundImage: `url(${slide.uri})`,
            backgroundSize: 'cover',
            backgroundPosition: 'center'
          }}
        />
      )}

      {/* Main image layer with optional Ken Burns effect */}
      <section
        className={`full-screen ${animationClass} ${kenBurnsClass} ${className}`}
        style={{
          backgroundImage: `url(${slide.uri})`,
          backgroundSize: zoomMode
        }}
      />
    </>
  );
}
