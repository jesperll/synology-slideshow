import React from 'react';
import { useSwipe } from '../hooks/useSwipe';
import { SwipeDirection } from '../types';

interface SwipeAreaProps {
  onSwipe: (direction: SwipeDirection) => void;
  className?: string;
  children: React.ReactNode;
}

export function SwipeArea({ onSwipe, className, children }: SwipeAreaProps) {
  const { handlePointerDown, handlePointerUp, handlePointerCancel } = useSwipe({ onSwipe });

  return (
    <div
      className={className}
      onPointerDown={handlePointerDown}
      onPointerUp={handlePointerUp}
      onPointerCancel={handlePointerCancel}
      style={{ touchAction: 'pan-y', overscrollBehavior: 'none' }}
    >
      {children}
    </div>
  );
}
