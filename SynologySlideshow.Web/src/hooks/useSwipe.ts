import { useRef } from 'react';
import { SwipeDirection } from '../types';

interface SwipeHandlers {
  onSwipe: (direction: SwipeDirection) => void;
}

export function useSwipe({ onSwipe }: SwipeHandlers) {
  const xDownRef = useRef<number | null>(null);
  const yDownRef = useRef<number | null>(null);

  const handleTouchStart = (e: React.TouchEvent) => {
    xDownRef.current = e.touches[0].clientX;
    yDownRef.current = e.touches[0].clientY;
  };

  const handleTouchEnd = (e: React.TouchEvent) => {
    if (xDownRef.current === null || yDownRef.current === null) {
      return;
    }

    const xDiff = xDownRef.current - e.changedTouches[0].clientX;
    const yDiff = yDownRef.current - e.changedTouches[0].clientY;

    if (Math.abs(xDiff) < 100 && Math.abs(yDiff) < 100) {
      xDownRef.current = null;
      yDownRef.current = null;
      return;
    }

    if (Math.abs(xDiff) > Math.abs(yDiff)) {
      if (xDiff > 0) {
        onSwipe(SwipeDirection.RightToLeft);
      } else {
        onSwipe(SwipeDirection.LeftToRight);
      }
    } else {
      if (yDiff > 0) {
        onSwipe(SwipeDirection.BottomToTop);
      } else {
        onSwipe(SwipeDirection.TopToBottom);
      }
    }

    xDownRef.current = null;
    yDownRef.current = null;
  };

  const handleTouchCancel = () => {
    xDownRef.current = null;
    yDownRef.current = null;
  };

  return { handleTouchStart, handleTouchEnd, handleTouchCancel };
}
