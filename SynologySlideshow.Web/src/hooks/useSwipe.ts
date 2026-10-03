import { useRef } from 'react';
import { SwipeDirection } from '../types';

interface SwipeHandlers {
  onSwipe: (direction: SwipeDirection) => void;
}

export function useSwipe({ onSwipe }: SwipeHandlers) {
  const xDownRef = useRef<number | null>(null);
  const yDownRef = useRef<number | null>(null);

  // Pointer events unify mouse, touch and pen input in one API, so dragging with
  // a mouse on desktop triggers the same swipe detection as a touch gesture.
  const handlePointerDown = (e: React.PointerEvent) => {
    xDownRef.current = e.clientX;
    yDownRef.current = e.clientY;
  };

  const handlePointerUp = (e: React.PointerEvent) => {
    if (xDownRef.current === null || yDownRef.current === null) {
      return;
    }

    const xDiff = xDownRef.current - e.clientX;
    const yDiff = yDownRef.current - e.clientY;

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

  const handlePointerCancel = () => {
    xDownRef.current = null;
    yDownRef.current = null;
  };

  return { handlePointerDown, handlePointerUp, handlePointerCancel };
}
