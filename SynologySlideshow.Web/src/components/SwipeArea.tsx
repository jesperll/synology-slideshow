import React from 'react';
import { useSwipe } from '../hooks/useSwipe';
import { SwipeDirection } from '../types';

interface SwipeAreaProps {
  onSwipe: (direction: SwipeDirection) => void;
  className?: string;
  children: React.ReactNode;
}

export function SwipeArea({ onSwipe, className, children }: SwipeAreaProps) {
  const { handleTouchStart, handleTouchEnd, handleTouchCancel } = useSwipe({ onSwipe });

  return (
    <div
      className={className}
      onTouchStart={handleTouchStart}
      onTouchEnd={handleTouchEnd}
      onTouchCancel={handleTouchCancel}
      style={{ touchAction: 'pan-y', overscrollBehavior: 'none' }}
    >
      {children}
    </div>
  );
}
